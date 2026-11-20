using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace WDSound.Web.Data;

public class Store(DbContextOptions<Store> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<SiteSettings> Settings => Set<SiteSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Entry>().HasIndex(x => new { x.Kind, x.Slug }).IsUnique();
        builder.Entity<Entry>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PackageItem>().HasKey(x => new { x.PackageId, x.ProductId });
        builder.Entity<Entry>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PackageId);
        builder.Entity<PackageItem>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EntryImage>().HasKey(x => new { x.EntryId, x.MediaId });
        builder.Entity<EntryImage>().HasOne(x => x.Media).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Variant>().Property(x => x.Price).HasPrecision(18, 2);
    }
}

public enum ContentKind { Home, About, Category, Product, Package, Gallery, Testimonial, Faq, Navigation }

public class Entry
{
    public int Id { get; set; }
    [ConcurrencyCheck] public Guid Revision { get; set; } = Guid.NewGuid();
    public ContentKind Kind { get; set; }
    [MaxLength(90)] public string Slug { get; set; } = "";
    [MaxLength(150)] public string TitleId { get; set; } = "";
    [MaxLength(150)] public string TitleEn { get; set; } = "";
    [MaxLength(12000)] public string BodyId { get; set; } = "";
    [MaxLength(12000)] public string BodyEn { get; set; } = "";
    [MaxLength(40)] public string? Target { get; set; }
    [MaxLength(4000)] public string? SpecsId { get; set; }
    [MaxLength(4000)] public string? SpecsEn { get; set; }
    [MaxLength(160)] public string? SeoId { get; set; }
    [MaxLength(160)] public string? SeoEn { get; set; }
    public bool Published { get; set; }
    public bool Featured { get; set; }
    public int Sort { get; set; }
    public int? CategoryId { get; set; }
    public Entry? Category { get; set; }
    public List<Variant> Variants { get; set; } = [];
    public List<PackageItem> Items { get; set; } = [];
    public List<EntryImage> Images { get; set; } = [];

    public string Title(string lang) => lang == "en" ? TitleEn : TitleId;
    public string Body(string lang) => lang == "en" ? BodyEn : BodyId;
    public string? Specifications(string lang) => lang == "en" ? SpecsEn : SpecsId;
    public string Description(string lang) => (lang == "en" ? SeoEn : SeoId) ?? Body(lang)[..Math.Min(160, Body(lang).Length)];
    public IEnumerable<(string Unit, decimal Amount)> StartingPrices() => Variants
        .Where(x => x.Active && x.Price > 0)
        .GroupBy(x => x.Unit)
        .Select(group => (group.Key, group.Min(x => x.Price)));
}

public class Variant
{
    public int Id { get; set; }
    [StringLength(100), Required] public string NameId { get; set; } = "";
    [StringLength(100), Required] public string NameEn { get; set; } = "";
    [RegularExpression("day|event|unit|package"), Required] public string Unit { get; set; } = "day";
    [Range(typeof(decimal), "1", "9999999999999999")] public decimal Price { get; set; }
    public bool Active { get; set; } = true;
    public string Name(string lang) => lang == "en" ? NameEn : NameId;
    public static string Money(decimal value, string unit, string lang)
    {
        var label = lang == "en" ? unit : unit switch { "day" => "hari", "event" => "acara", "package" => "paket", _ => "unit" };
        return $"Rp {value.ToString("N0", CultureInfo.GetCultureInfo(lang == "en" ? "en-US" : "id-ID"))} / {label}";
    }
}

public class PackageItem
{
    public int PackageId { get; set; }
    public int ProductId { get; set; }
    public Entry Product { get; set; } = null!;
    public int Quantity { get; set; }
}

public class Media
{
    public int Id { get; set; }
    [MaxLength(40)] public string FileName { get; set; } = "";
    [MaxLength(150)] public string AltId { get; set; } = "";
    [MaxLength(150)] public string AltEn { get; set; } = "";
    public string Alt(string lang) => lang == "en" ? AltEn : AltId;
}

public class EntryImage
{
    public int EntryId { get; set; }
    public int MediaId { get; set; }
    public Media Media { get; set; } = null!;
    public int Sort { get; set; }
}

public class Inquiry
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(2000)] public string Message { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool Reviewed { get; set; }
}

public class SiteSettings
{
    public int Id { get; set; } = 1;
    [RegularExpression(@"[1-9][0-9]{6,14}")] public string? WhatsApp { get; set; }
    [EmailAddress, MaxLength(254)] public string? Email { get; set; }
    [MaxLength(200)] public string? Address { get; set; }
    [MaxLength(120)] public string? Hours { get; set; }
    [MaxLength(300)] public string? FooterId { get; set; }
    [MaxLength(300)] public string? FooterEn { get; set; }
    [RegularExpression(@"G-[A-Z0-9]+"), MaxLength(32)] public string? AnalyticsId { get; set; }
    [RegularExpression(@"[A-Za-z0-9_-]+"), MaxLength(120)] public string? SearchVerification { get; set; }
}
