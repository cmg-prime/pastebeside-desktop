using System.ComponentModel.DataAnnotations;
using System.Text;
using PasteBeside.HandshakeHub;

public static class ParticipantIdentifier
{
    private static int _minLengthInclusive = 3;
    private static int _maxLengthExclusive = 9;
    private static double _vowelChance = 0.5;
    private static string _vowels = "aeiouy";
    private static string _consonants = "bcdfghjklmnpqrstvwxz";

    public static string New()
    {
        var random = new Random();
        var length = random.Next(_minLengthInclusive, _maxLengthExclusive);
        var stringBuilder = new StringBuilder();
        for(var nameIndex = 0; nameIndex < length; nameIndex++)
        {
            var source = random.NextDouble() < _vowelChance ? _vowels : _consonants;
            var characterIndex = random.Next(0, source.Length);
            stringBuilder.Append(source[characterIndex]);
        }
        stringBuilder.Append(DateTime.Now.ToString("-HHmmss"));
        var identifier = stringBuilder.ToString();
        return string.Concat(identifier[0].ToString().ToUpper(), identifier.AsSpan(1));
    }
}