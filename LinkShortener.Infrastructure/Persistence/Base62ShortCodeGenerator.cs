using LinkShortener.Domain.Interfaces;

namespace LinkShortener.Infrastructure.Persistence;

public class Base62ShortCodeGenerator : IShortCodeGenerator
{
    private static readonly char[] Base62Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();
    private readonly Random _random = new();

    public string Generate()
    {
        // 7 karakterli rastgele ve tahmin edilmesi zor bir Base62 kod üretiyoruz
        var code = new char[7];
        for (int i = 0; i < code.Length; i++)
        {
            code[i] = Base62Chars[_random.Next(62)];
        }
        return new string(code);
    }
}
