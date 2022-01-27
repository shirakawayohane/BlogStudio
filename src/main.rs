use chrono::DateTime;
use chrono::Utc;
use pulldown_cmark::{html, Options, Parser};
use std::env;
use std::fs;
use std::path::Path;
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
    let input_path = args.get(1).unwrap();
    let output_path = args.get(2).unwrap();

    println!("{}", input_path);

    let mut options = Options::empty();
    options.insert(Options::ENABLE_STRIKETHROUGH);
    options.insert(Options::ENABLE_TABLES);
    options.insert(Options::ENABLE_HEADING_ATTRIBUTES);
    options.insert(Options::ENABLE_FOOTNOTES);

    let mut articles = Vec::new();

    for dir_entry in fs::read_dir(input_path).unwrap() {
        let path = dir_entry.unwrap().path();
        let _file_content = fs::read_to_string(path.clone()).unwrap(); // 実際はStringなので、ちゃんと左辺値に束縛しておく
        let file_meta = fs::metadata(path).unwrap();
        let file_content = _file_content.as_str();
        // let starts_with = file_content.starts_with("---");
        // dbg!(starts_with);
        let (metadata_input, markdown_input) = if file_content.starts_with("---") {
            let close_index = file_content[3..]
                .find("---")
                .expect("メタデータの閉じタグが見つかりません。");

            (
                Some(&file_content[3..close_index + 1]),
                &file_content[4 + close_index..],
            )
        } else {
            (None, file_content)
        };

        metadata_input.expect("メタデータは必須です。");

        let yamls = match metadata_input {
            None => None,
            Some(input) => Some(
                YamlLoader::load_from_str(input).expect("メタデータはYAMLの書式に従ってください"),
            ),
        }
        .unwrap();
        let metadata = &yamls[0];

        let title = metadata["title"]
            .as_str()
            .expect("メタデータの 'title' は必須項目です")
            .to_string();
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

    let template = fs::read_to_string("template.html").unwrap();

    let _ = fs::remove_dir_all(output_path);
    let _ = fs::create_dir(output_path);

    for article in articles {
        let path_str = format!("{}/{}.html", output_path, article.title);
        let path = Path::new(&path_str);
        let exists = Path::exists(path);
        if exists {
            panic!("記事名が被っています: {}", article.title);
        }
        let html = template.replace(
            "<article />",
            &format!(
                "<article>
            {}
</article>",
                article.content
            ),
        );
        print!("{}/{}.html", output_path, article.title);
        fs::write(path, html).unwrap();
    }
}
