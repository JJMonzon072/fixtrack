using FixTrack.Domain.Common;

namespace FixTrack.Tests.Domain;

public sealed class ContactRulesTests
{
    [Theory]
    [InlineData("María José Pérez")]
    [InlineData("Taller 3D S.A.")]
    [InlineData("O'Connor & Asociados")]
    [InlineData("Peña-Núñez")]
    public void Accepts_valid_names(string name) => Assert.Null(ContactRules.ValidateName(name));

    [Theory]
    [InlineData("", "El nombre es obligatorio.")]
    [InlineData("Al", "El nombre debe tener entre 3 y 150 caracteres.")]
    [InlineData("123", "El nombre debe contener al menos dos letras.")]
    [InlineData("Ana <script>", "El nombre contiene caracteres no permitidos.")]
    public void Rejects_invalid_names(string name, string expected) => Assert.Equal(expected, ContactRules.ValidateName(name));

    [Fact]
    public void Rejects_names_longer_than_the_limit() =>
        Assert.NotNull(ContactRules.ValidateName(new string('a', ContactRules.NameMaxLength + 1)));

    [Theory]
    [InlineData("5555-1234")]
    [InlineData("+502 5555 1234")]
    [InlineData("(502) 2222-3333")]
    [InlineData("2234567")]
    public void Accepts_valid_phones(string phone) => Assert.Null(ContactRules.ValidatePhone(phone));

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("1234567890123456")]
    [InlineData("5555-12ab")]
    [InlineData("55+55 1234")]
    public void Rejects_invalid_phones(string phone) => Assert.NotNull(ContactRules.ValidatePhone(phone));

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ana.lopez+taller@correo.example.gt")]
    public void Accepts_valid_emails(string email) => Assert.Null(ContactRules.ValidateEmail(email));

    [Theory]
    [InlineData("ana")]
    [InlineData("ana@")]
    [InlineData("ana@example")]
    [InlineData("ana@@example.com")]
    [InlineData("ana lopez@example.com")]
    [InlineData("ana@example.com.")]
    public void Rejects_invalid_emails(string email) => Assert.NotNull(ContactRules.ValidateEmail(email));

    [Fact]
    public void Normalizes_emails_to_trimmed_lowercase()
    {
        Assert.Equal("ana@example.com", ContactRules.NormalizeEmail("  Ana@Example.COM "));
        Assert.Null(ContactRules.NormalizeEmail("   "));
    }

    [Fact]
    public void Cleans_extra_whitespace() => Assert.Equal("Ana María López", Text.Clean("  Ana   María\tLópez "));
}
