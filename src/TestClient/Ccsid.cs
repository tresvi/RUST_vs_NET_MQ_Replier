using System.Text;

namespace NetMqReplier;

/// <summary>
/// Traduce un CCSID de MQ a un <see cref="Encoding"/> de .NET.
/// Para la mayoría de los CCSID el número coincide con la code page de Windows;
/// se mapean a mano los que no (UTF-8, ISO-8859-1, UTF-16).
/// </summary>
public static class Ccsid
{
    static Ccsid()
    {
        // Habilita code pages no Unicode (1252, 850, EBCDIC 37/500/1047, etc.)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding GetEncoding(int ccsid) => ccsid switch
    {
        1208 => Encoding.UTF8,
        819 => Encoding.Latin1,
        1200 or 13488 or 17584 => Encoding.BigEndianUnicode,
        _ => Encoding.GetEncoding(ccsid),
    };
}
