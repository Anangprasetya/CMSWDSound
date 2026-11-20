using System.ComponentModel.DataAnnotations;
using WDSound.Web.Controllers;
using WDSound.Web.Data;

namespace WDSound.UnitTests;

public class CatalogTests
{
    [Fact]
    public void StartingPricesCompareOnlyActiveVariantsWithMatchingUnits()
    {
        var product = new Entry
        {
            Variants =
            [
                new() { Unit = "day", Price = 175000 },
                new() { Unit = "day", Price = 150000 },
                new() { Unit = "event", Price = 275000 },
                new() { Unit = "day", Price = 1, Active = false },
                new() { Unit = "day", Price = 0 }
            ]
        };

        Assert.Equal(new[] { ("day", 150000m), ("event", 275000m) }, product.StartingPrices());
    }

    [Theory]
    [InlineData("speaker-aktif", true)]
    [InlineData("Speaker Aktif", false)]
    [InlineData("../speaker", false)]
    [InlineData("speaker--aktif", false)]
    public void SlugMustBeSuitableForACatalogUrl(string slug, bool expected)
    {
        var input = new EntryInput { Slug = slug, TitleId = "Speaker Aktif" };
        Assert.Equal(expected, Validator.TryValidateObject(input, new ValidationContext(input), null, true));
    }

    [Theory]
    [InlineData("", "Message", false)]
    [InlineData("Visitor", " ", false)]
    [InlineData("Visitor", "Message", true)]
    public void ContactRequiresNameAndMessage(string name, string message, bool expected)
    {
        var input = new ContactInput { Name = name, Message = message };
        Assert.Equal(expected, Validator.TryValidateObject(input, new ValidationContext(input), null, true));
    }

    [Fact]
    public void InquiryEncodesTheItemAndKeepsLanguageOnDetailPage()
    {
        var site = new PublicPage("en", "products", "https://example.test",
            new SiteSettings { WhatsApp = "628000000000" }, [])
        {
            Entry = new Entry { Kind = ContentKind.Product, Slug = "speaker", TitleEn = "Speaker & amplifier" }
        };

        Assert.Equal("/id/products/speaker", site.AlternatePath);
        Assert.Contains("Speaker%20%26%20amplifier", site.WhatsApp(site.Entry));
    }
}
