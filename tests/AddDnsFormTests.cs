using System.Collections.Generic;
using System.Windows.Forms;
using Xunit;

namespace FastSwitchDNS.Tests
{
    public class AddDnsFormTests : EnvTest
    {
        static string Validate(string name, string primary, string secondary = "", string extra = "", string doh = "") =>
            AddDnsForm.Validate(name, primary, secondary, extra, doh, out _, out _);

        [Fact]
        public void Validate_FullEntry()
        {
            var error = AddDnsForm.Validate(" My=DNS ", "1.2.3.4", "5.6.7.8", "2001:db8::1, 2001:db8::2",
                " https://dns.example.com/dns-query ", out var preset, out _);
            Assert.Null(error);
            Assert.Equal("MyDNS", preset.Name); // "=" would break the file format
            Assert.Equal(new[] { "1.2.3.4", "5.6.7.8", "2001:db8::1", "2001:db8::2" }, preset.Servers);
            Assert.Equal("https://dns.example.com/dns-query", preset.DohTemplate);
            Assert.False(preset.BuiltIn);
        }

        [Fact]
        public void Validate_EmptyTemplateMeansNoDoh()
        {
            Assert.Null(AddDnsForm.Validate("Home", "1.2.3.4", null, null, "  ", out var preset, out _));
            Assert.Null(preset.DohTemplate);
            Assert.False(preset.HasDoh);
        }

        [Theory]
        [InlineData("", "1.2.3.4", "", "Enter a name.", "Name")]
        [InlineData("=", "1.2.3.4", "", "Enter a name.", "Name")]
        [InlineData("google", "1.2.3.4", "", "This name is already used by a built-in preset.", "Name")]
        [InlineData("Home", "  ", "", "Enter the primary DNS.", "Primary")]
        [InlineData("Home", "1.2.3", "", "Invalid IP address: 1.2.3", "Primary")]
        [InlineData("Home", "1.2.3.4", "http://dns.example.com/q",
            "Invalid DoH template: http://dns.example.com/q\nUse an address like https://dns.example.com/dns-query", "Doh")]
        public void Validate_Errors(string name, string primary, string doh, string message, string field)
        {
            var error = AddDnsForm.Validate(name, primary, "", "", doh, out var preset, out var actualField);
            Assert.Equal(message, error);
            Assert.Equal(field, actualField.ToString());
            Assert.Null(preset);
        }

        [Fact]
        public void Validate_BadSecondaryReportsIt() =>
            Assert.Equal("Invalid IP address: nope", Validate("Home", "1.2.3.4", "nope"));

        [Fact]
        public void Validate_NullsAreSafe() =>
            Assert.Equal("Enter a name.", AddDnsForm.Validate(null, null, null, null, null, out _, out _));

        [Fact]
        public void Form_HasAllFields() => Ui(() =>
        {
            using var form = new AddDnsForm();
            Assert.Equal("Custom DNS", form.Text);
            var labels = new List<string>();
            void Walk(Control c) { foreach (Control x in c.Controls) { if (x is Label) labels.Add(x.Text); Walk(x); } }
            Walk(form);
            Assert.Contains("Name:", labels);
            Assert.Contains("Primary DNS:", labels);
            Assert.Contains("DoH template:", labels);
            Assert.NotNull(form.AcceptButton);
            Assert.NotNull(form.CancelButton);
        });

        [Fact]
        public void Form_Windows10_HasNoDohField() => Ui(() =>
        {
            SetWindowsBuild(Windows10);
            using var form = new AddDnsForm();
            var labels = new List<string>();
            void Walk(Control c) { foreach (Control x in c.Controls) { if (x is Label) labels.Add(x.Text); Walk(x); } }
            Walk(form);
            Assert.Contains("Primary DNS:", labels);
            Assert.DoesNotContain("DoH template:", labels);

            form.NameBox.Text = "Home";
            form.PrimaryBox.Text = "1.2.3.4";
            form.Submit(applyNow: false);
            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.Null(form.Result.DohTemplate);
        });

        [Fact]
        public void Submit_Valid_SetsResultAndCloses() => Ui(() =>
        {
            using var form = new AddDnsForm();
            form.NameBox.Text = "Home";
            form.PrimaryBox.Text = "1.2.3.4";
            form.SecondaryBox.Text = "5.6.7.8";
            form.ExtraBox.Text = "";
            form.DohBox.Text = "https://dns.example.com/dns-query";

            form.Submit(applyNow: true);

            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.True(form.ApplyNow);
            Assert.Equal("Home", form.Result.Name);
            Assert.Equal("https://dns.example.com/dns-query", form.Result.DohTemplate);
        });

        [Fact]
        public void Submit_SaveOnly() => Ui(() =>
        {
            using var form = new AddDnsForm();
            form.NameBox.Text = "Home";
            form.PrimaryBox.Text = "1.2.3.4";
            form.Submit(applyNow: false);
            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.False(form.ApplyNow);
        });

        [Fact]
        public void Submit_Invalid_WarnsAndStaysOpen() => Ui(() =>
        {
            using var form = new AddDnsForm();
            string warning = null;
            form.ShowWarning = m => warning = m;
            form.NameBox.Text = "Home";
            form.PrimaryBox.Text = "1.2.3.4";
            form.DohBox.Text = "not a url";

            form.Submit(applyNow: true);

            Assert.StartsWith("Invalid DoH template: not a url", warning);
            Assert.Equal(DialogResult.None, form.DialogResult);
            Assert.Null(form.Result);
        });
    }
}
