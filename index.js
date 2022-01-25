
const NEUM_INSET = "neum-inset";
if (location.href.includes("blog")) {
    document.getElementById("nav-blog").classList.add(NEUM_INSET);
} else if (location.href.includes("artifacts")) {
    document.getElementById("nav-artifacts").classList.add(NEUM_INSET);
} else if (location.href.includes("profile")) {
    console.log("classlist add neum-inset");
    document.getElementById("nav-profile").classList.add(NEUM_INSET);
}

console.log("hoge");