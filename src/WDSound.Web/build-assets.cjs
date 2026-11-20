const fs = require("node:fs");
const path = require("node:path");
const assets = {
    "bootstrap.min.css": "bootstrap/dist/css/bootstrap.min.css",
    "alpine.min.js": "alpinejs/dist/cdn.min.js",
};
const destination = path.join(__dirname, "wwwroot", "vendor");
fs.mkdirSync(destination, { recursive: true });
for (const [target, source] of Object.entries(assets)) {
    fs.copyFileSync(path.join(__dirname, "node_modules", source), path.join(destination, target));
}
