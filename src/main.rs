use pulldown_cmark::{html, Options, Parser};
use std::env;
use std::fs;
use std::ops::Deref;
use yaml_rust::YamlLoader;

#[derive(Debug)]
struct Article {
    title: String,
    created_at: String,
    updated_at: String,
    categories: Vec<String>,
    html: String,
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

    for path in fs::read_dir(input_path).unwrap() {
        let _file_content = fs::read_to_string(path.unwrap().path()).unwrap(); // 実際はStringなので、ちゃんと左辺値に束縛しておく
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
        let created_at = metadata["created_at"]
            .as_str()
            .expect("メタデータの 'created_at' は必須項目です")
            .to_string();
        let updated_at = metadata["updated_at"]
            .as_str()
            .expect("メタデータの 'updated_at' は必須項目です")
            .to_string();
        let categories = metadata["categories"]
            .as_vec()
            .expect("categoriesは配列型である必要があります。")
            .iter()
            .map(|x| {
                x.as_str()
                    .expect("カテゴリは文字列で表記してください (null非許容)")
                    .to_string()
            })
            .collect();

        let parser = Parser::new_ext(markdown_input, options);
        let mut html = String::new();
        html::push_html(&mut html, parser);

        articles.push(Article {
            title,
            created_at,
            updated_at,
            categories,
            html,
        });
    }

    dbg!(articles);
}
