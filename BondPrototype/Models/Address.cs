namespace BondPrototype.Models;

public class Address
{
    public string City { get; set; }
    public string Country { get; set; }

    public string FullAddressString => $"{City}, {Country}";
} 