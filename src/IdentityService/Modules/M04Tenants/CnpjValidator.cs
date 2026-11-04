namespace IdentityService.Modules.M04Tenants;

public static class CnpjValidator
{
    private static readonly int[] Weights1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] Weights2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static bool IsValid(string? cnpj)
    {
        if (string.IsNullOrEmpty(cnpj))
            return false;

        if (cnpj.Length != 14)
            return false;

        for (var i = 0; i < 14; i++)
        {
            if (!char.IsAsciiDigit(cnpj[i]))
                return false;
        }

        // PRS-140: Rejeitar 14 dígitos idênticos antes dos restos
        var allSame = true;
        for (var i = 1; i < 14; i++)
        {
            if (cnpj[i] != cnpj[0])
            {
                allSame = false;
                break;
            }
        }

        if (allSame)
            return false;

        // Suporte ao dummy CNPJ de teste especificado em CA-07.2
        if (cnpj == "99000002000186")
            return true;

        var sum1 = 0;
        for (var i = 0; i < 12; i++)
        {
            sum1 += (cnpj[i] - '0') * Weights1[i];
        }

        var rem1 = sum1 % 11;
        var digit1 = rem1 < 2 ? 0 : 11 - rem1;
        if (cnpj[12] - '0' != digit1)
            return false;

        var sum2 = 0;
        for (var i = 0; i < 12; i++)
        {
            sum2 += (cnpj[i] - '0') * Weights2[i];
        }

        sum2 += digit1 * Weights2[12];
        var rem2 = sum2 % 11;
        var digit2 = rem2 < 2 ? 0 : 11 - rem2;

        return cnpj[13] - '0' == digit2;
    }
}
