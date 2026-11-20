using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using WDSound.Web.Data;

namespace WDSound.Web.Controllers;

[Authorize(Roles = "Admin,Editor")]
[Route("admin")]
public class AdminController(Store db, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, LocalMedia media) : Controller
{
    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet("login"), AllowAnonymous]
    public IActionResult Login() => View(new LoginInput());

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await signIn.PasswordSignInAsync(input.Email.Trim(), input.Password, false, lockoutOnFailure: true);
            if (result.Succeeded) return RedirectToAction(nameof(Index));
            ModelState.AddModelError("", "Email atau kata sandi tidak valid, atau akun terkunci sementara.");
        }
        return View(input);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet("denied")]
    public IActionResult Denied() => StatusCode(403);

    [HttpGet("")]
    public async Task<IActionResult> Index(ContentKind? kind)
    {
        var entries = db.Entries.AsNoTracking().AsQueryable();
        if (kind.HasValue) entries = entries.Where(x => x.Kind == kind);
        ViewBag.Kind = kind;
        ViewBag.Inquiries = IsAdmin ? await db.Inquiries.CountAsync(x => !x.Reviewed) : 0;
        return View(await entries.OrderBy(x => x.Kind).ThenBy(x => x.Sort).ThenBy(x => x.Id).ToListAsync());
    }

    [HttpGet("edit/{id:int?}")]
    public async Task<IActionResult> Edit(int? id, ContentKind kind = ContentKind.Product)
    {
        if (!Enum.IsDefined(kind)) return BadRequest();
        if (id is null) return await Editor(new EntryInput { Kind = kind });
        var entry = await Find(id.Value);
        if (entry is null) return NotFound();
        if (entry.Published && !IsAdmin) return Forbid();
        return await Editor(new EntryInput
        {
            Id = entry.Id, Revision = entry.Revision, Kind = entry.Kind, Slug = entry.Slug,
            TitleId = entry.TitleId, TitleEn = entry.TitleEn, BodyId = entry.BodyId, BodyEn = entry.BodyEn,
            Target = entry.Target, SpecsId = entry.SpecsId, SpecsEn = entry.SpecsEn, SeoId = entry.SeoId, SeoEn = entry.SeoEn, Published = entry.Published, Featured = entry.Featured,
            Sort = entry.Sort, CategoryId = entry.CategoryId,
            MediaIds = string.Join(",", entry.Images.OrderBy(x => x.Sort).Select(x => x.MediaId)),
            Items = string.Join("\n", entry.Items.Select(x => $"{x.ProductId}|{x.Quantity}")),
            Variants = string.Join("\n", entry.Variants.OrderBy(x => x.Id).Select(x =>
                $"{x.NameId}|{x.NameEn}|{x.Unit}|{x.Price.ToString(CultureInfo.InvariantCulture)}|{(x.Active ? 1 : 0)}"))
        });
    }

    [HttpPost("edit/{id:int?}"), RequestSizeLimit(96 * 1024)]
    public async Task<IActionResult> Edit([FromRoute] int? id, EntryInput input)
    {
        if (input.Id != id || !Enum.IsDefined(input.Kind)) return BadRequest();
        if (!IsAdmin && input.Published) return Forbid();
        if (!ModelState.IsValid) return await Editor(input);
        var entry = id.HasValue ? await Find(id.Value) : new Entry { Kind = input.Kind };
        if (entry is null) return NotFound();
        if (entry.Published && !IsAdmin) return Forbid();
        if (entry.Kind != input.Kind) return BadRequest();
        if (id.HasValue && entry.Revision != input.Revision)
            return Conflict("Konten telah berubah. Buka kembali sebelum menyimpan.");
        if (entry.Published && entry.Slug != input.Slug)
            ModelState.AddModelError(nameof(input.Slug), "Batalkan publikasi sebelum mengubah slug.");
        if (await db.Entries.AnyAsync(x => x.Kind == input.Kind && x.Slug == input.Slug && x.Id != entry.Id))
            ModelState.AddModelError(nameof(input.Slug), "Slug sudah digunakan.");
        var homeCta = input.Kind == ContentKind.Home && input.Slug is ("primary-cta" or "secondary-cta");
        if (input.Kind == ContentKind.Home && input.Slug is not ("home" or "primary-cta" or "secondary-cta" or "products" or "packages" or "gallery" or "testimonials" or "faq" or "contact"))
            ModelState.AddModelError(nameof(input.Slug), "Pilih slug beranda yang tersedia.");
        if (homeCta && (input.Target is null || input.Target is not ("home" or "contact") && !SiteController.Sections.ContainsKey(input.Target)))
            ModelState.AddModelError(nameof(input.Target), "Pilih halaman tujuan untuk tombol.");
        if (input.Kind == ContentKind.Navigation && input.Slug is not ("home" or "contact") && !SiteController.Sections.ContainsKey(input.Slug))
            ModelState.AddModelError(nameof(input.Slug), "Gunakan slug halaman internal yang tersedia.");
        if (input.Published && (string.IsNullOrWhiteSpace(input.TitleEn) ||
            (input.Kind != ContentKind.Navigation && (input.Kind != ContentKind.Home || input.Slug == "home") && (string.IsNullOrWhiteSpace(input.BodyId) || string.IsNullOrWhiteSpace(input.BodyEn)))))
            ModelState.AddModelError("", "Lengkapi judul dan isi dalam kedua bahasa sebelum publikasi.");
        if (input.Kind == ContentKind.Product &&
            !await db.Entries.AnyAsync(x => x.Id == input.CategoryId && x.Kind == ContentKind.Category && (!input.Published || x.Published)))
            ModelState.AddModelError(nameof(input.CategoryId), "Pilih kategori yang valid. Produk terbit memerlukan kategori terbit.");
        if (entry.Published && !input.Published && input.Kind == ContentKind.Category &&
            await db.Entries.AnyAsync(x => x.CategoryId == entry.Id && x.Published))
            ModelState.AddModelError("", "Batalkan publikasi produk dalam kategori ini terlebih dahulu.");

        var variants = input.Kind == ContentKind.Product ? ParseVariants(input.Variants) : [];
        var items = input.Kind == ContentKind.Package ? ParseItems(input.Items) : [];
        if (items.Count > 0 && await db.Entries.CountAsync(x => items.Keys.Contains(x.Id) && x.Kind == ContentKind.Product
                    && (!input.Published || x.Published && x.Category!.Published)) != items.Count)
            ModelState.AddModelError(nameof(input.Items), "Pilih produk valid dan terbit untuk paket yang akan dipublikasikan.");
        var images = ParseMedia(input.MediaIds);
        if (images.Count > 0 && await db.Media.CountAsync(x => images.Contains(x.Id)) != images.Count)
            ModelState.AddModelError(nameof(input.MediaIds), "Ada ID media yang tidak tersedia.");
        if (input.Published && input.Kind is (ContentKind.Gallery or ContentKind.Product) && images.Count == 0)
            ModelState.AddModelError(nameof(input.MediaIds), "Produk dan galeri memerlukan gambar sebelum terbit.");
        if (!ModelState.IsValid) return await Editor(input);

        entry.Revision = Guid.NewGuid();
        entry.Slug = input.Slug;
        entry.TitleId = input.TitleId.Trim();
        entry.TitleEn = input.TitleEn?.Trim() ?? "";
        entry.BodyId = input.BodyId?.Trim() ?? "";
        entry.BodyEn = input.BodyEn?.Trim() ?? "";
        entry.Target = homeCta ? input.Target : null;
        entry.SpecsId = input.Kind == ContentKind.Product ? input.SpecsId?.Trim() : null;
        entry.SpecsEn = input.Kind == ContentKind.Product ? input.SpecsEn?.Trim() : null;
        entry.SeoId = input.SeoId;
        entry.SeoEn = input.SeoEn;
        entry.Sort = input.Sort;
        entry.Published = input.Published;
        entry.Featured = input.Featured;
        entry.CategoryId = input.Kind == ContentKind.Product ? input.CategoryId : null;
        entry.Variants.Clear();
        entry.Variants.AddRange(variants);
        entry.Items.RemoveAll(x => !items.ContainsKey(x.ProductId));
        foreach (var item in items)
        {
            var existing = entry.Items.Find(x => x.ProductId == item.Key);
            if (existing is null) entry.Items.Add(new PackageItem { ProductId = item.Key, Quantity = item.Value });
            else existing.Quantity = item.Value;
        }
        entry.Images.RemoveAll(x => !images.Contains(x.MediaId));
        for (var i = 0; i < images.Count; i++)
        {
            var existing = entry.Images.Find(x => x.MediaId == images[i]);
            if (existing is null) entry.Images.Add(new EntryImage { MediaId = images[i], Sort = i });
            else existing.Sort = i;
        }
        if (id is null) db.Entries.Add(entry);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict("Konten telah berubah. Buka kembali sebelum menyimpan."); }
        catch (DbUpdateException) { return Conflict("Konten berbenturan dengan perubahan lain. Periksa slug dan referensi lalu coba lagi."); }
        return RedirectToAction(nameof(Index), new { kind = entry.Kind });
    }

    [HttpPost("delete/{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, Guid revision)
    {
        var entry = await db.Entries.FindAsync(id);
        if (entry is null) return NotFound();
        if (entry.Published || entry.Revision != revision)
            return Conflict("Hanya draf dengan versi terbaru yang dapat dihapus.");
        db.Entries.Remove(entry);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict("Konten telah berubah atau masih digunakan."); }
        return RedirectToAction(nameof(Index), new { kind = entry.Kind });
    }

    [HttpGet("settings"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Settings() => View(await db.Settings.AsNoTracking().SingleAsync());

    [HttpPost("settings"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Settings(SiteSettings input)
    {
        if (!ModelState.IsValid) return View(input);
        input.Id = 1;
        db.Settings.Update(input);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet("inquiries"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Inquiries() => View(await db.Inquiries.AsNoTracking().OrderByDescending(x => x.Id).ToListAsync());

    [HttpPost("inquiries/{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Review(int id)
    {
        var inquiry = await db.Inquiries.FindAsync(id);
        if (inquiry is null) return NotFound();
        inquiry.Reviewed = true;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Inquiries));
    }

    [HttpGet("media")]
    public async Task<IActionResult> Media() => View(await db.Media.AsNoTracking().OrderByDescending(x => x.Id).ToListAsync());

    [HttpPost("media"), RequestSizeLimit(LocalMedia.MaxSize + 32_768)]
    public async Task<IActionResult> Upload(IFormFile? image, string? altId, string? altEn, CancellationToken cancellation)
    {
        if (image is null || string.IsNullOrWhiteSpace(altId) || string.IsNullOrWhiteSpace(altEn) || altId.Length > 150 || altEn.Length > 150)
            return BadRequest("Gambar dan teks alternatif dalam kedua bahasa wajib diisi (maksimal 150 karakter).");
        var name = await media.SaveAsync(image, cancellation);
        if (name is null) return BadRequest("Unggah gambar JPG, PNG, atau WebP maksimal 5 MB.");
        db.Media.Add(new Media { FileName = name, AltId = altId.Trim(), AltEn = altEn.Trim() });
        try { await db.SaveChangesAsync(cancellation); }
        catch { media.Delete(name); throw; }
        return RedirectToAction(nameof(Media));
    }

    [HttpPost("media/{id:int}/delete"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteMedia(int id)
    {
        var image = await db.Media.FindAsync(id);
        if (image is null) return NotFound();
        db.Media.Remove(image);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict("Gambar masih digunakan."); }
        media.Delete(image.FileName);
        return RedirectToAction(nameof(Media));
    }

    [HttpGet("users"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Accounts()
    {
        ViewBag.Accounts = await users.Users.AsNoTracking().OrderBy(x => x.Email).ToListAsync();
        ViewBag.Admins = (await users.GetUsersInRoleAsync("Admin")).Select(x => x.Id).ToHashSet();
        return View(new AccountInput());
    }

    [HttpPost("users"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Accounts(AccountInput input)
    {
        if (!ModelState.IsValid) return await Accounts();
        var user = new IdentityUser(input.Email.Trim()) { Email = input.Email.Trim() };
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, input.Role);
        if (result.Succeeded)
        {
            await transaction.CommitAsync();
            return RedirectToAction(nameof(Accounts));
        }
        await transaction.RollbackAsync();
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return await Accounts();
    }

    [HttpPost("users/{id}/role"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChangeRole(string id, string role)
    {
        if (role is not ("Admin" or "Editor") || id == users.GetUserId(User)) return BadRequest();
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var previous = role == "Admin" ? "Editor" : "Admin";
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!await users.IsInRoleAsync(user, role) && !(await users.AddToRoleAsync(user, role)).Succeeded) return Conflict();
        if (await users.IsInRoleAsync(user, previous) && !(await users.RemoveFromRoleAsync(user, previous)).Succeeded) return Conflict();
        if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return Conflict();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Accounts));
    }

    private Task<Entry?> Find(int id) => db.Entries.Include(x => x.Variants).Include(x => x.Items)
        .Include(x => x.Images).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id);

    private async Task<IActionResult> Editor(EntryInput input)
    {
        ViewBag.Categories = await db.Entries.AsNoTracking().Where(x => x.Kind == ContentKind.Category).OrderBy(x => x.TitleId).ToListAsync();
        ViewBag.Products = await db.Entries.AsNoTracking().Where(x => x.Kind == ContentKind.Product).OrderBy(x => x.TitleId).ToListAsync();
        return View("Edit", input);
    }

    private List<Variant> ParseVariants(string? lines)
    {
        List<Variant> variants = [];
        foreach (var line in Lines(lines))
        {
            var fields = line.Split('|', StringSplitOptions.TrimEntries);
            if (fields.Length != 5 || fields[0].Length is < 1 or > 100 || fields[1].Length is < 1 or > 100 ||
                fields[2] is not ("day" or "event" or "unit" or "package") ||
                !decimal.TryParse(fields[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) ||
                price is < 1 or > 9999999999999999m || decimal.Round(price, 2) != price || fields[4] is not ("1" or "0"))
            {
                ModelState.AddModelError(nameof(EntryInput.Variants), $"Format varian tidak valid: {line}");
                continue;
            }
            variants.Add(new Variant { NameId = fields[0], NameEn = fields[1], Unit = fields[2], Price = price, Active = fields[4] == "1" });
        }
        return variants;
    }

    private Dictionary<int, int> ParseItems(string? lines)
    {
        Dictionary<int, int> items = [];
        foreach (var line in Lines(lines))
        {
            var fields = line.Split('|', StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || !int.TryParse(fields[0], out var id) || id <= 0 ||
                !int.TryParse(fields[1], out var quantity) || quantity <= 0 || !items.TryAdd(id, quantity))
                ModelState.AddModelError(nameof(EntryInput.Items), $"Komposisi tidak valid: {line}");
        }
        return items;
    }

    private List<int> ParseMedia(string? ids)
    {
        List<int> images = [];
        foreach (var value in (ids ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(value, out var id) || id <= 0 || images.Contains(id))
                ModelState.AddModelError(nameof(EntryInput.MediaIds), "Gunakan ID media yang unik, dipisahkan koma.");
            else images.Add(id);
        }
        return images;
    }

    private static string[] Lines(string? value) => (value ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public class EntryInput
{
    public int? Id { get; set; }
    public Guid Revision { get; set; }
    public ContentKind Kind { get; set; }
    [Required, StringLength(90), RegularExpression("[a-z0-9]+(?:-[a-z0-9]+)*")] public string Slug { get; set; } = "";
    [Required, StringLength(150)] public string TitleId { get; set; } = "";
    [StringLength(150)] public string? TitleEn { get; set; }
    [StringLength(12000)] public string? BodyId { get; set; }
    [StringLength(12000)] public string? BodyEn { get; set; }
    [StringLength(40)] public string? Target { get; set; }
    [StringLength(4000)] public string? SpecsId { get; set; }
    [StringLength(4000)] public string? SpecsEn { get; set; }
    [StringLength(160)] public string? SeoId { get; set; }
    [StringLength(160)] public string? SeoEn { get; set; }
    public bool Published { get; set; }
    public bool Featured { get; set; }
    public int Sort { get; set; }
    public int? CategoryId { get; set; }
    [StringLength(2000)] public string? MediaIds { get; set; }
    [StringLength(8000)] public string? Variants { get; set; }
    [StringLength(2000)] public string? Items { get; set; }
}

public class LoginInput
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128)] public string Password { get; set; } = "";
}

public class AccountInput : LoginInput
{
    [Required, RegularExpression("Admin|Editor")] public string Role { get; set; } = "Editor";
}
