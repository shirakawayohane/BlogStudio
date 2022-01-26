use pulldown_cmark::{html, Options, Parser};
use std::env;
use std::fs;
use yaml_rust::{YamlLoader};

fn main() {
    let args: Vec<String> = env::args().collect();
    let input_path = args.get(1).unwrap();
    let output_path = args.get(2).unwrap();

    println!("{}", input_path);

    let mut options = Options::empty();
    options.insert(Options::ENABLE_STRIKETHROUGH);
    options.insert(Options::ENABLE_TABLES);
    options.insert(Options::ENABLE_HEADING_ATTRIBUTES);

    for path in fs::read_dir(input_path).unwrap() {
        let _file_content = fs::read_to_string(path.unwrap().path()).unwrap(); // 実際はStringなので、ちゃんと左辺値に束縛しておく
        let file_content = _file_content.as_str();
        // let starts_with = file_content.starts_with("---");
        // dbg!(starts_with);
        let (metadata_input, markdown_input) = if file_content.starts_with("---") {
            let close_index = file_content[3..].find("---").expect("メタデータの閉じタグが見つかりません。");

            (Some(&file_content[3..close_index + 1]), &file_content[4 + close_index..])
        } else {
            (None, file_content)
        };

        let metadata = match metadata_input {
            None => None,
            Some(input) => Some(YamlLoader::load_from_str(input).expect("メタデータはYAMLの書式に従ってください"))
        };
        
        // let parser = Parser::new_ext(markdown_input, options);
        // let mut html_output = String::new();
        // html::push_html(&mut html_output, parser);
    }
}
