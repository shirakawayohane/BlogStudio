use chrono::DateTime;
use chrono::Utc;
use notify::DebouncedEvent;
use notify::RecursiveMode;
use notify::Watcher;
use pulldown_cmark::{html, Options, Parser};
use std::env;
use std::fs;
use std::path::Path;
use std::sync::mpsc::channel;
use std::thread;
use std::time::Duration;
use warp::Future;
use yaml_rust::YamlLoader;

#[derive(Debug)]
struct Article {
    // ユニークな識別子。URLの一部にもなる場所
    title: String,
    // ファイルから自動で取る
    created_at: DateTime<Utc>,
    // ファイルから自動で取る
    updated_at: DateTime<Utc>,
    // 無かったら自動で ["Others"] にする
    categories: Vec<String>,
    // articleタグの中身
    content: String,
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let output_dir = args.get(1).unwrap().clone();

    let mut options = Options::empty();
    options.insert(Options::ENABLE_STRIKETHROUGH);
    options.insert(Options::ENABLE_TABLES);
    options.insert(Options::ENABLE_HEADING_ATTRIBUTES);
    options.insert(Options::ENABLE_FOOTNOTES);

    let mut articles = Vec::new();

    for dir_entry in fs::read_dir("articles").unwrap() {
        let path = dir_entry.unwrap().path();
        let _file_content = fs::read_to_string(path.clone()).unwrap(); // 実際はStringなので、ちゃんと左辺値に束縛しておく
        let title = Path::file_name(&path)
            .unwrap()
            .to_str()
            .unwrap()
            .split(".")
            .next()
            .unwrap()
            .to_string();
        let file_meta = fs::metadata(path).unwrap();
        let mut file_content = _file_content.as_str();
        // let starts_with = file_content.starts_with("---");
        // dbg!(starts_with);
        let (metadata_input, markdown_input) = if file_content.starts_with("---") {
            file_content = &file_content[3..];
            let close_index = file_content
                .find("---")
                .expect("メタデータの閉じタグが見つかりません。");

            (
                Some(&file_content[..close_index]),
                &file_content[3 + close_index..],
            )
        } else {
            (None, file_content)
        };

        let yamls = match metadata_input {
            None => None,
            Some(input) => Some(
                YamlLoader::load_from_str(input).expect("メタデータはYAMLの書式に従ってください"),
            ),
        }
        .unwrap();
        let metadata = &yamls[0];

        let created_at: DateTime<Utc> = file_meta.created().unwrap().into();
        let updated_at: DateTime<Utc> = file_meta.created().unwrap().into();
        let categories = match metadata["categories"].as_vec() {
            Some(x) => x
                .iter()
                .map(|x| {
                    x.as_str()
                        .expect("カテゴリは文字列で表記してください (null非許容)")
                        .to_string()
                })
                .collect::<Vec<String>>(),
            None => vec!["Others".to_string()],
        };

        let parser = Parser::new_ext(markdown_input, options);
        let mut article = String::new();
        html::push_html(&mut article, parser);

        articles.push(Article {
            title,
            created_at,
            updated_at,
            categories,
            content: article,
        });
    }

    articles.sort_by(|a, b| a.created_at.cmp(&b.created_at));

    if Path::exists(Path::new(&output_dir)) {
        fs::remove_dir_all(&output_dir).unwrap();
    }
    fs::create_dir(&output_dir).unwrap();

    // 静的ファイルのコピー
    for file in fs::read_dir("wwwroot").unwrap() {
        let path = file.unwrap().path();
        if path.file_name().unwrap() == "template.html" {
            continue;
        }
        let output_path = format!(
            "{}/{}",
            &output_dir,
            path.file_name().unwrap().to_str().unwrap()
        );
        println!("{}", output_path);
        fs::copy(path, output_path).unwrap();
    }

    generate_articles(&output_dir, &articles);

    if let Some(arg2) = args.get(2) {
        if arg2 == "--watch" {
            println!("Starting http server...");

            // Httpサーバーの用意。loop内でFutureをポーリングする。
            let routes = warp::fs::dir(output_dir.clone());
            let http_server_task = warp::serve(routes).run(([127, 0, 0, 1], 8080));

            // wwwroot内のファイルを監視し、変更があり次第対象ファイルを更新する。
            // let (watch_sender, watch_receiver) = channel();
            let (watch_sender, watch_receiver) = channel();
            let mut watcher = notify::watcher(watch_sender, Duration::from_secs(10)).unwrap();
            watcher.watch("wwwroot", RecursiveMode::Recursive).unwrap();
            let _output_dir = output_dir.clone();

            let watch_task = thread::spawn(move || match watch_receiver.recv() {
                Ok(event) => match event {
                    DebouncedEvent::Write(path) | DebouncedEvent::Create(path) => {
                        if path.ends_with("template.html") {
                            generate_articles(&output_dir, &articles);
                            return;
                        }
                        let from = path.clone();
                        let to = format!(
                            "{}/{}",
                            _output_dir,
                            path.file_name().unwrap().to_str().unwrap()
                        );

                        println!("copying file from {} to {}", from.to_str().unwrap(), to);

                        fs::copy(from, to).unwrap();
                    }
                    _ => {
                        dbg!(event);
                    }
                },
                _ => {}
            });

            futures::executor::block_on(http_server_task);
            watch_task.join().unwrap();
        }
    }
}

fn generate_articles(output_dir: &str, articles: &Vec<Article>) {
    println!("Generating articles...");
    let template = fs::read_to_string("wwwroot/template.html").unwrap();
    fs::create_dir(format!("{}/articles", output_dir)).unwrap();
    for article in articles {
        let path_str = format!("{}/articles/{}.html", output_dir, article.title);
        let path = Path::new(&path_str);
        let exists = Path::exists(path);
        if exists {
            panic!("記事名が被っています: {}", article.title);
        }
        // OGP
        // メタデータで指定されていたらURLをそのまま入れる。
        // されていなかったらデフォルトのアイコンを入れる。
        let html = template
            .replace(
                "<article></article>",
                &format!(
                    "<article>
                {}
    </article>",
                    article.content
                ),
            )
            .replace(
                "<ogp></ogp>",
                &format!(
                    r#"
<meta property="og:url" content="https://wizlite.jp">
<meta property="og:type content="website">
<meta property="og:title" content="{}">
<meta property="og:image" content="ogp.png">
<meta property="og:site_name" content="Neuromancy">
<meta name="twitter:card" content="summary" />
<meta name="twitter:site" content="@wizlightyear" />
<meta name="twitter:player" content="@wizlightyear" />
                "#,
                    article.title
                ),
            );
        fs::write(path, html).unwrap();
    }
}
