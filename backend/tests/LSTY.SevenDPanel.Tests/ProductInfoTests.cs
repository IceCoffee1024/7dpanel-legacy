using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ProductInfoTests
    {
        [Fact]
        public void Name_matches_product_name()
        {
            Assert.Equal("7DPanel", ProductInfo.Name);
        }

        [Fact]
        public void Version_matches_mod_metadata()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "ModInfo.xml");
            var document = XDocument.Load(path);
            var version = document.Root?.Element("Version")?.Attribute("value")?.Value;

            Assert.Equal(ProductInfo.Version, version);
        }

        [Fact]
        public void Mod_name_is_valid_for_game_loader()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "ModInfo.xml");
            var document = XDocument.Load(path);
            var name = document.Root?.Element("Name")?.Attribute("value")?.Value;

            Assert.Matches(new Regex("^[0-9a-zA-Z_-]+$"), name);
        }
    }
}
