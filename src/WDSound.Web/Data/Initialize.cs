using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace WDSound.Web.Data;

public static class Initialize
{
    public static async Task Run(IServiceProvider services, IConfiguration configuration, bool development, bool sqlite)
    {
        var db = services.GetRequiredService<Store>();
        if (sqlite) await db.Database.EnsureCreatedAsync();
        else await db.Database.MigrateAsync();

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in new[] { "Admin", "Editor" })
        {
            if (!await roles.RoleExistsAsync(name)) Ensure(await roles.CreateAsync(new IdentityRole(name)));
        }

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if ((email is null) != (password is null))
            throw new InvalidOperationException("Configure both BootstrapAdmin:Email and BootstrapAdmin:Password.");
        if (email is not null && !(await users.GetUsersInRoleAsync("Admin")).Any())
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new IdentityUser(email) { Email = email };
                Ensure(await users.CreateAsync(user, password!));
            }
            else if (!await users.CheckPasswordAsync(user, password!))
            {
                throw new InvalidOperationException("Bootstrap administrator credentials do not match the existing account.");
            }
            Ensure(await users.AddToRoleAsync(user, "Admin"));
        }

        if (!await db.Settings.AnyAsync()) db.Settings.Add(new SiteSettings());
        if (development && configuration.GetValue<bool>("Demo:Seed") && !await db.Entries.AnyAsync())
        {
            var category = new Entry { Kind = ContentKind.Category, Slug = "audio", TitleId = "Audio", TitleEn = "Audio", BodyId = "Peralatan audio acara.", BodyEn = "Event audio equipment.", Published = true };
            db.Entries.AddRange(category,
                new Entry { Kind = ContentKind.Home, Slug = "home", TitleId = "Suara yang menghidupkan acara.", TitleEn = "Sound that brings events to life.", BodyId = "Sewa perlengkapan audio untuk pernikahan, pertemuan, dan acara di Jawa Timur.", BodyEn = "Audio rental for weddings, meetings, and events in East Java.", Published = true },
                new Entry { Kind = ContentKind.About, Slug = "about", TitleId = "Tentang WD Sound", TitleEn = "About WD Sound", BodyId = "Wandi Sound System menyediakan penyewaan peralatan acara di Jawa Timur.", BodyEn = "Wandi Sound System provides event equipment rental in East Java.", Published = true },
                new Entry { Kind = ContentKind.Product, Slug = "speaker-aktif", TitleId = "Speaker Aktif", TitleEn = "Active Speaker", BodyId = "Contoh produk untuk pengujian katalog.", BodyEn = "Sample product for catalog testing.", Category = category, Featured = true, Published = true,
                    Variants = [new() { NameId = "Standar", NameEn = "Standard", Price = 250000 }] },
                new Entry { Kind = ContentKind.Package, Slug = "acara-intim", TitleId = "Paket Acara Intim", TitleEn = "Intimate Event Package", BodyId = "Contoh paket audio untuk acara kecil.", BodyEn = "Sample audio package for small events.", Featured = true, Published = true },
                new Entry { Kind = ContentKind.Faq, Slug = "area-layanan", TitleId = "Di mana area layanan WD Sound?", TitleEn = "Where does WD Sound operate?", BodyId = "Jawa Timur.", BodyEn = "East Java.", Published = true });
        }
        await db.SaveChangesAsync();
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(", ", result.Errors.Select(x => x.Code)));
    }
}
