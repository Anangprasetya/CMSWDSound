"""HTTP acceptance checks against a fresh Development database."""
import base64
import http.cookiejar
import os
import re
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("WDSOUND_TEST_URL", "http://127.0.0.1:5182")
ADMIN = os.environ["BootstrapAdmin__Email"]
PASSWORD = os.environ["BootstrapAdmin__Password"]
EDITOR = "editor-smoke@example.test"
EDITOR_PASSWORD = os.environ.get("WDSOUND_TEST_EDITOR_PASSWORD", "EditorSmoke-Only-123!")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None


def session():
    return urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(client, path, status=200, fields=None, data=None, headers=None):
    if fields is not None:
        data = urllib.parse.urlencode(fields).encode()
    try:
        response = client.open(urllib.request.Request(BASE + path, data=data, headers=headers or {}), timeout=15)
    except urllib.error.HTTPError as exception:
        response = exception
    with response:
        body = response.read().decode(errors="replace")
        assert response.status == status, (path, response.status, status, body[:1200])
        return body, response.headers


def token(body):
    return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)[1]


def post(client, path, fields, status=302, form=None):
    return call(client, path, status, fields={**fields,
                "__RequestVerificationToken": token(call(client, form or path)[0])})


def login(client, email, password):
    post(client, "/admin/login", {"Email": email, "Password": password})


def entry(kind, slug, name, **fields):
    return {"Kind": kind, "Slug": slug, "TitleId": name, "TitleEn": name + " EN",
            "BodyId": "Deskripsi contoh", "BodyEn": "Test description", **fields}


def upload(client, content, filename, status=302):
    boundary = "WD-test-boundary"
    parts = {"__RequestVerificationToken": token(call(client, "/admin/media")[0]),
             "altId": "Gambar uji", "altEn": "Test image"}
    body = b"".join(
        f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode()
        for key, value in parts.items()
    )
    body += (f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="{filename}"\r\n'
             'Content-Type: image/png\r\n\r\n').encode() + content + f"\r\n--{boundary}--\r\n".encode()
    return call(client, "/admin/media", status, data=body,
                headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})


def find_id(client, label):
    row = re.search(r"<tr>\s*<td>" + re.escape(label) + r"</td>.*?</tr>", call(client, "/admin")[0], re.S)
    assert row, label
    return re.search(r"/admin/edit/(\d+)", row[0])[1]


def edit(client, id, fields, status=302):
    path = "/admin/edit/" + id
    body, _ = call(client, path)
    revision = re.search(r'id="Revision"[^>]*value="([^"]+)"', body)[1]
    return post(client, path, {**fields, "Id": id, "Revision": revision}, status)


def main():
    visitor, admin, editor = session(), session(), session()
    assert call(visitor, "/", 302)[1]["Location"] == "/id"
    assert 'lang="id"' in call(visitor, "/id")[0]
    for page in ("about", "products", "packages", "gallery", "testimonials", "faq", "contact"):
        assert 'lang="en"' in call(visitor, f"/en/{page}")[0]
    call(visitor, "/vendor/bootstrap.min.css")
    call(visitor, "/images/stage.webp")
    call(visitor, "/admin", 302)
    call(visitor, "/id/contact", 400, fields={"Name": "No token", "Message": "Must be rejected"})
    assert "Masukkan nama" in post(visitor, "/id/contact", {"Name": "", "Message": ""}, 200)[0]
    post(visitor, "/id/contact", {"Name": "Pelanggan uji", "Message": "<script>message</script>"})
    assert "Pesan Anda sudah tersimpan" in call(visitor, "/id/contact")[0]

    login(admin, ADMIN, PASSWORD)
    inquiries, _ = call(admin, "/admin/inquiries")
    assert "Pelanggan uji" in inquiries and "&lt;script&gt;message&lt;/script&gt;" in inquiries
    post(admin, "/admin/edit", {"Kind": "Home", "Slug": "primary-cta", "TitleId": "Pilih paket audio",
         "TitleEn": "Explore audio packages", "Target": "packages", "Published": "true"}, form="/admin/edit?kind=Home")
    assert 'href="/en/packages">Explore audio packages' in call(visitor, "/en")[0]
    call(admin, "/admin/settings")
    post(admin, "/admin/settings", {"WhatsApp": "628000000000", "Email": "info@example.test"})
    post(admin, "/admin/users", {"Email": EDITOR, "Password": EDITOR_PASSWORD, "Role": "Editor"})
    login(editor, EDITOR, EDITOR_PASSWORD)
    call(editor, "/admin/inquiries", 302)
    call(editor, "/admin/settings", 302)
    call(editor, "/admin/users", 302)

    upload(editor, b"<script>invalid image</script>", "fake.png", 400)
    png = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jK2QAAAAASUVORK5CYII=")
    upload(editor, png, "pixel.png")
    media_id = re.search(r'/media/(\d+)"', call(editor, "/admin/media")[0])[1]
    call(visitor, f"/media/{media_id}", 404)
    call(editor, f"/media/{media_id}")

    category = entry("Category", "test-audio", "Kategori Uji", Published="true")
    post(admin, "/admin/edit", category, form="/admin/edit?kind=Category")
    category_id = find_id(admin, "Kategori Uji")
    product = entry("Product", "wd-test-speaker", "Speaker Uji", CategoryId=category_id, MediaIds=media_id,
                    SpecsEn="Test specifications", Variants="Kecil|Small|day|110000|1\nBesar|Large|day|200000|1\nAcara|Event|event|70000|1\nNonaktif|Hidden|day|500|0")
    post(editor, "/admin/edit", product, form="/admin/edit?kind=Product")
    product_id = find_id(admin, "Speaker Uji")
    call(visitor, "/id/products/wd-test-speaker", 404)
    edit(editor, product_id, product)
    edit(editor, product_id, {**product, "Published": "true"}, 302)
    invalid = edit(admin, product_id, {**product, "Published": "true", "TitleEn": ""}, 200)[0]
    assert "Lengkapi judul" in invalid
    invalid = edit(admin, product_id, {**product, "Published": "true", "Variants": "bad"}, 200)[0]
    assert "Format varian tidak valid" in invalid
    edit(admin, product_id, {**product, "Published": "true"})
    call(editor, f"/admin/edit/{product_id}", 302)
    published = call(visitor, "/en/products/wd-test-speaker")[0]
    assert "Speaker Uji EN" in published and "110,000 / day" in published and "70,000 / event" in published
    assert "Test specifications" in published and "Hidden" not in published and "wa.me/628000000000" in published and "mailto:info@example.test" in published
    assert 'href="/id/products/wd-test-speaker"' in published
    assert "Speaker Uji" in call(visitor, "/id/products?category=test-audio")[0]
    call(visitor, f"/media/{media_id}")

    package = entry("Package", "wd-test-package", "Paket Uji", Items=f"{product_id}|2", Published="true")
    invalid = post(admin, "/admin/edit", {**package, "Items": f"{product_id}|0"}, 200,
                   form="/admin/edit?kind=Package")[0]
    assert "Komposisi tidak valid" in invalid
    post(admin, "/admin/edit", package, form="/admin/edit?kind=Package")
    package_id = find_id(admin, "Paket Uji")
    assert "× 2" in call(visitor, "/en/packages/wd-test-package")[0]
    edit(admin, package_id, {**package, "Published": "false"})
    call(visitor, "/en/packages/wd-test-package", 404)
    post(admin, f"/admin/delete/{package_id}",
         {"revision": re.search(r'id="Revision"[^>]*value="([^"]+)"', call(admin, f"/admin/edit/{package_id}")[0])[1]},
         form=f"/admin/edit/{package_id}")

    assert "wd-test-speaker" in call(visitor, "/sitemap.xml")[0]
    assert "Disallow: /admin" in call(visitor, "/robots.txt")[0]
    post(admin, "/admin/logout", {}, form="/admin")
    call(admin, "/admin", 302)
    print("Smoke tests passed: public routes, both languages, catalog/pricing, admin/editor, publishing, media, settings, contact, CSRF, and SEO.")


if __name__ == "__main__":
    main()
