using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WDSound.Web.Data;

namespace WDSound.Web.Controllers;

public class SiteController(Store db, LocalMedia media, IConfiguration configuration) : Controller
{
    internal static readonly Dictionary<string, ContentKind> Sections = new()
    {
        ["about"] = ContentKind.About, ["products"] = ContentKind.Product,
        ["packages"] = ContentKind.Package, ["gallery"] = ContentKind.Gallery,
        ["testimonials"] = ContentKind.Testimonial, ["faq"] = ContentKind.Faq
    };

    private IQueryable<Entry> PublicEntries => db.Entries.AsNoTracking()
        .Where(x => x.Published && (x.Category == null || x.Category.Published));

    [HttpGet("/")]
    public IActionResult Root() => Redirect("/id");

    [HttpGet("/{lang:regex(^(id|en)$)}")]
    [HttpGet("/{lang:regex(^(id|en)$)}/{section}")]
    public async Task<IActionResult> Page(string lang, string? section, string? category)
    {
        section ??= "home";
        var model = await Prepare(lang, section);
        if (section == "home")
        {
            model.Entry = await WithVisuals(PublicEntries.Where(x => x.Kind == ContentKind.Home && x.Slug == "home")).SingleOrDefaultAsync();
            model.HomeSections = await PublicEntries.Where(x => x.Kind == ContentKind.Home && x.Slug != "home").ToDictionaryAsync(x => x.Slug);
            model.Entries = await Featured(ContentKind.Product, 3);
            model.Packages = await Featured(ContentKind.Package, 3);
            model.Gallery = await Featured(ContentKind.Gallery, 3);
            model.Questions = await PublicEntries.Where(x => x.Kind == ContentKind.Faq).OrderBy(x => x.Sort).Take(3).ToListAsync();
            model.Introduction = await PublicEntries.Where(x => x.Kind == ContentKind.About).OrderBy(x => x.Sort).FirstOrDefaultAsync();
            model.Testimonials = await PublicEntries.Where(x => x.Kind == ContentKind.Testimonial).OrderBy(x => x.Sort).Take(3).ToListAsync();
        }
        else if (Sections.TryGetValue(section, out var kind))
        {
            var entries = PublicEntries.Where(x => x.Kind == kind);
            if (kind == ContentKind.Product)
            {
                model.Categories = await PublicEntries.Where(x => x.Kind == ContentKind.Category).OrderBy(x => x.Sort).ToListAsync();
                model.Category = category;
                if (!string.IsNullOrEmpty(category)) entries = entries.Where(x => x.Category != null && x.Category.Slug == category);
            }
            model.Entries = await WithVisuals(entries).OrderBy(x => x.Sort).ThenBy(x => x.Id).ToListAsync();
            if (kind == ContentKind.About) model.Entry = model.Entries.FirstOrDefault();
        }
        else if (section != "contact") return NotFound();
        return View(model);
    }

    [HttpGet("/{lang:regex(^(id|en)$)}/{section}/{slug}")]
    public async Task<IActionResult> Detail(string lang, string section, string slug)
    {
        if (section is not ("products" or "packages")) return NotFound();
        var entry = await WithVisuals(PublicEntries.Where(x => x.Kind == Sections[section] && x.Slug == slug))
            .Include(x => x.Items.Where(item => item.Product.Published && (item.Product.Category == null || item.Product.Category.Published)))
            .ThenInclude(x => x.Product).SingleOrDefaultAsync();
        if (entry is null) return NotFound();
        var model = await Prepare(lang, section);
        model.Entry = entry;
        return View(model);
    }

    [HttpPost("/{lang:regex(^(id|en)$)}/contact"), EnableRateLimiting("contact"), RequestSizeLimit(16_384)]
    public async Task<IActionResult> Contact(string lang, ContactInput input)
    {
        if (!ModelState.IsValid)
        {
            var model = await Prepare(lang, "contact");
            model.Contact = input;
            ModelState.AddModelError("", model.T("Masukkan nama (maksimal 100 karakter) dan pesan (maksimal 2.000 karakter).", "Enter a name (up to 100 characters) and a message (up to 2,000 characters)."));
            return View("Page", model);
        }
        if (string.IsNullOrEmpty(input.Website))
        {
            db.Inquiries.Add(new Inquiry { Name = input.Name.Trim(), Message = input.Message.Trim() });
            await db.SaveChangesAsync();
        }
        TempData["ContactSaved"] = true;
        return Redirect($"/{lang}/contact");
    }

    [HttpGet("/media/{id:int}")]
    public async Task<IActionResult> Image(int id)
    {
        var entry = await db.Media.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null) return NotFound();
        var published = await PublicEntries.AnyAsync(x => x.Images.Any(image => image.MediaId == id));
        if (!published && !(User.IsInRole("Admin") || User.IsInRole("Editor"))) return NotFound();
        var path = media.PathFor(entry.FileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, Path.GetExtension(path) switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" });
    }

    [HttpGet("/robots.txt")]
    public IActionResult Robots()
    {
        var origin = Origin();
        return Content("User-agent: *\nDisallow: /admin\n" + (origin == "" ? "" : $"Sitemap: {origin}/sitemap.xml\n"), "text/plain");
    }

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var origin = Origin();
        if (origin == "") return NotFound();
        var entries = await PublicEntries.Where(x => x.Kind == ContentKind.Product || x.Kind == ContentKind.Package)
            .Select(x => new { x.Kind, x.Slug }).ToListAsync();
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var paths = from lang in new[] { "id", "en" }
                    from page in new[] { "", "/contact" }.Concat(Sections.Keys.Select(key => "/" + key))
                        .Concat(entries.Select(entry => (entry.Kind == ContentKind.Product ? "/products/" : "/packages/") + entry.Slug))
                    select new XElement(ns + "url", new XElement(ns + "loc", $"{origin}/{lang}{page}"));
        return Content(new XElement(ns + "urlset", paths).ToString(), "application/xml");
    }

    [Route("/error")]
    public IActionResult Error() => Problem(statusCode: 500);

    private string Origin()
    {
        return Uri.TryCreate(configuration["Site:Origin"], UriKind.Absolute, out var uri) &&
            uri.Scheme is "https" or "http" ? uri.GetLeftPart(UriPartial.Authority) : "";
    }

    private async Task<PublicPage> Prepare(string lang, string section)
    {
        return new PublicPage(lang, section, Origin(), await db.Settings.AsNoTracking().SingleAsync(),
            await PublicEntries.Where(x => x.Kind == ContentKind.Navigation).OrderBy(x => x.Sort).ToListAsync());
    }

    private Task<List<Entry>> Featured(ContentKind kind, int count) => WithVisuals(PublicEntries.Where(x => x.Featured && x.Kind == kind))
        .OrderBy(x => x.Sort).ThenBy(x => x.Id).Take(count).ToListAsync();

    private static IQueryable<Entry> WithVisuals(IQueryable<Entry> query) => query
        .Include(x => x.Images).ThenInclude(x => x.Media).Include(x => x.Variants).AsSplitQuery();
}

public class ContactInput
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(2000)] public string Message { get; set; } = "";
    public string? Website { get; set; }
}

public record PublicPage(string Lang, string Section, string Origin, SiteSettings Settings, List<Entry> Navigation)
{
    public Entry? Entry { get; set; }
    public List<Entry> Entries { get; set; } = [];
    public Dictionary<string, Entry> HomeSections { get; set; } = [];
    public string HomeTitle(string key, string id, string en) => HomeSections.GetValueOrDefault(key)?.Title(Lang) ?? T(id, en);
    public string HomeTarget(string key, string fallback) => Path(HomeSections.GetValueOrDefault(key)?.Target ?? fallback);
    public Entry? Introduction { get; set; }
    public List<Entry> Packages { get; set; } = [];
    public List<Entry> Gallery { get; set; } = [];
    public List<Entry> Questions { get; set; } = [];
    public List<Entry> Testimonials { get; set; } = [];
    public List<Entry> Categories { get; set; } = [];
    public string? Category { get; set; }
    public ContactInput Contact { get; set; } = new();
    public string T(string id, string en) => Lang == "en" ? en : id;
    public string Label(string section) => section switch
    {
        "home" => T("Beranda", "Home"), "about" => T("Tentang", "About"),
        "products" => T("Peralatan", "Equipment"), "packages" => T("Paket", "Packages"),
        "gallery" => T("Galeri", "Gallery"), "testimonials" => T("Testimoni", "Testimonials"),
        "faq" => "FAQ", _ => T("Kontak", "Contact")
    };
    public string Path(string section, string? slug = null) => $"/{Lang}" + (section == "home" ? "" : $"/{section}") + (slug is null ? "" : "/" + slug);
    public string CurrentPath => Path(Section, Entry?.Kind is ContentKind.Product or ContentKind.Package ? Entry.Slug : null);
    public string AlternatePath => "/" + T("en", "id") + CurrentPath[3..];

    public string? WhatsApp(Entry? entry = null)
    {
        if (Settings.WhatsApp is not { } number || !Regex.IsMatch(number, @"^[1-9][0-9]{6,14}$")) return null;
        return $"https://wa.me/{number}?text={Uri.EscapeDataString(Message(entry))}";
    }

    public string? Email(Entry? entry = null) => string.IsNullOrEmpty(Settings.Email) ? null :
        $"mailto:{Settings.Email}?subject={Uri.EscapeDataString(T("Pertanyaan sewa", "Rental inquiry"))}&body={Uri.EscapeDataString(Message(entry))}";

    private string Message(Entry? entry)
    {
        var message = T("Halo WD Sound, saya ingin bertanya tentang penyewaan.", "Hello WD Sound, I have a rental inquiry.");
        return entry is null ? message : $"{message}\n{entry.Title(Lang)}\n{Origin}{CurrentPath}";
    }
}
