using System;
using Seruni22Bakery.Domain.Entities;

Console.WriteLine("BCrypt hash for admin123:");
var hash = User.HashPassword("admin123");
Console.WriteLine(hash);
Console.WriteLine("Verify: " + BCrypt.Net.BCrypt.Verify("admin123", hash));
