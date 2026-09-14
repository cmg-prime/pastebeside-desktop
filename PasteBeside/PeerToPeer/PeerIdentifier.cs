namespace PasteBeside.PeerToPeer;

public static class PeerIdentifier
{
    private static IList<string> _adjectives = ["Verdant", "Unctuous", "Incandescent", "Aerial", "Heroic", "Unorthodox", "Spiritual", "Dynastic", "Incalculable", "Chivalrous", "Perilous", "Swift", "Maritime", "Eldritch", "Spicy", "Bellicose", "Abyssal", "Alert", "Aberrant", "Crepuscular", "Defective", "Repressed", "Ornate", "Dauntless", "Fancy", "Faithful", "Faithless", "Glamorous", "Greedy", "Harsh", "Impolitic", "Magnificent", "Gargantuan", "Miniscule", "Noxious", "Obnoxious", "Malevolent", "Avaricious", "Arbitrary", "Arcane", "Archaic", "Artless", "Audacious", "Benign", "Bombastic", "Brazen", "Capricious", "Caustic", "Captivating", "Deft", "Desultory", "Eccentric", "Eminent", "Transient", "Exacting", "Iconic", "Immutable", "Implacable", "Inchoate", "Inimical", "Inscrutable", "Gleaming", "Gloaming", "Laudable", "Luminous", "Mercurial", "Misanthropic", "Atavistic", "Nascent", "Obdurate", "Occluded", "Ostentatious", "Pervasive", "Polar", "Antecedent", "Pristine", "Relentless", "Reverent", "Subversive", "Timorous", "Volatile", "Gilded", "Unnatural", "Aleatory", "Revelatory"];
    private static IList<string> _nouns = ["Exodus", "Quandary", "Idol", "Atavism", "Avatar", "Abomination", "Anarchy", "Antagonist", "Dusk", "Monotony", "Treachery", "Perseverance", "Precursor", "Rhetoric", "Sycophant", "Zeal", "Cauchemar", "Daydream", "Fortress", "Obligation", "Hazard", "Venture", "Peril", "Jurisprudence", "Bastion", "Elegy", "Accolade", "Ascetic", "Cacophony", "Axiom", "Heresy", "Quiescence", "Aberration", "Anomaly", "Paean", "Perfidy", "Hegemony", "Anachronism", "Hubris", "Neophyte", "Shard", "Anathema", "Apotheosis", "Augury", "Exegesis", "Exodus", "Manifestation", "Convolution", "Threat", "Ember", "Oubliette", "Oath", "Objuration", "Ruination", "Devotion", "Morality", "Saga", "Apostle", "Epistle", "Reliquary", "Sanctuary", "Transcendence", "Devastation", "Abyss", "Void", "Paradise", "Euphoria", "Immolation", "Revelation", "Occlusion", "Conflagration", "Sin", "Purgatory", "Dervish", "Legion", "Changeling", "Prayer", "Calamity", "Incarnation", "Weapon", "Faith", "Blasphemer", "Outcast", "Tyrant", "Prophet", "Beast", "Mythos", "Manuscript", "Maelstrom"];

    public static string New()
    {
        var random = new Random();
        var randomAdjectiveIndex = random.Next(0, _adjectives.Count);
        var randomNounIndex = random.Next(0, _nouns.Count);

        var adjective = _adjectives[randomAdjectiveIndex];
        var noun = _nouns[randomNounIndex];
        return $"{adjective}-{noun}-{random.Next(0, 10000)}";
    }
}