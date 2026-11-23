using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Nutrition;

/// <summary>FR-ALG-01: the nine major US allergens plus gluten. The code is stored as text.</summary>
public enum AllergenCode
{
    Milk,
    Egg,
    Fish,
    Shellfish,
    TreeNuts,
    Peanuts,
    Wheat,
    Soy,
    Sesame,
    Gluten,
}

/// <summary>Section 8.2 RecipeAllergen.Status.</summary>
public enum AllergenStatus
{
    Contains,
    MayContain,
    NotPresent,
}

/// <summary>Where a recipe's allergen row came from (BR-52).</summary>
public enum AllergenOrigin
{
    Auto,
    Manual,
}

/// <summary>
/// FR-ALG-01, FR-ALG-02, BR-52, BR-53, AC-56, ADR 0036: word rules that detect allergens in an ingredient line, merging
/// per recipe, and manual overrides. Words match whole (with plurals); phrases such as "coconut milk" are removed for
/// the allergen they would wrongly trigger; "gluten-free", "vegan" and the like remove what they promise to leave out.
/// </summary>
public static class AllergenRules
{
    public const int RulesVersion = 1;

    public static IReadOnlyList<AllergenCode> All { get; } = Enum.GetValues<AllergenCode>();

    public static string Label(AllergenCode code) => code switch
    {
        AllergenCode.Milk => "Milk",
        AllergenCode.Egg => "Egg",
        AllergenCode.Fish => "Fish",
        AllergenCode.Shellfish => "Crustacean shellfish",
        AllergenCode.TreeNuts => "Tree nuts",
        AllergenCode.Peanuts => "Peanuts",
        AllergenCode.Wheat => "Wheat",
        AllergenCode.Soy => "Soy",
        AllergenCode.Sesame => "Sesame",
        AllergenCode.Gluten => "Gluten",
        _ => code.ToString(),
    };

    private static readonly string[] WheatWords =
    [
        "wheat", "flour", "bread", "breadcrumb", "bread crumb", "panko", "pasta", "spaghetti", "linguine", "fettuccine", "penne",
        "rigatoni", "macaroni", "lasagna", "lasagne", "noodle", "orzo", "couscous", "semolina", "durum", "farro", "bulgur",
        "spelt", "seitan", "tortilla", "pita", "bagel", "croissant", "cracker", "pie crust", "pastry", "phyllo", "filo",
        "biscuit", "roux", "matzo", "udon", "ramen", "wonton", "dumpling wrapper", "soy sauce", "shoyu", "teriyaki", "brioche",
        "baguette", "bun", "roll", "crouton", "graham", "pretzel", "gnocchi", "tempura", "pancake mix", "cake mix", "wheat germ",
    ];

    private static readonly string[] NotWheat =
    [
        "almond flour", "coconut flour", "rice flour", "corn flour", "oat flour", "chickpea flour", "gram flour", "buckwheat flour",
        "tapioca flour", "cassava flour", "potato flour", "sorghum flour", "teff flour", "millet flour", "arrowroot flour",
        "rice noodle", "glass noodle", "cellophane noodle", "bean thread noodle", "rice pasta", "corn tortilla", "rice cracker",
        "spring roll wrapper", "rice paper", "lettuce roll", "egg roll wrapper",
    ];

    private sealed record Rule(AllergenCode Code, AllergenStatus Status, Regex Include, Regex? Exclude);

    private static Regex Words(IEnumerable<string> words) =>
        new(@"\b(?:" + string.Join('|', words.OrderByDescending(w => w.Length).Select(w => Regex.Escape(w).Replace(@"\ ", @"\s+", StringComparison.Ordinal)))
            + @")(?:s|es)?\b", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    private static Rule R(AllergenCode code, AllergenStatus status, string[] include, string[]? exclude = null) =>
        new(code, status, Words(include), exclude is null ? null : Words(exclude));

    private static readonly Rule[] Rules =
    [
        R(AllergenCode.Milk, AllergenStatus.Contains,
            ["milk", "butter", "buttermilk", "cream", "cheese", "cheddar", "parmesan", "parmigiano", "pecorino", "mozzarella", "ricotta",
             "feta", "brie", "gouda", "gruyere", "mascarpone", "yogurt", "yoghurt", "whey", "casein", "ghee", "kefir", "half and half",
             "creme fraiche", "custard", "paneer", "queso", "burrata", "provolone", "monterey jack", "colby", "swiss", "neufchatel", "lactose", "pesto", "nutella"],
            ["coconut milk", "coconut cream", "almond milk", "oat milk", "soy milk", "soymilk", "rice milk", "cashew milk", "hemp milk", "plant milk",
             "peanut butter", "almond butter", "cashew butter", "nut butter", "sunflower butter", "seed butter", "apple butter", "cocoa butter",
             "shea butter", "cream of tartar", "cream of coconut", "ice cream maker", "milk thistle", "swiss chard"]),
        R(AllergenCode.Milk, AllergenStatus.MayContain, ["chocolate", "chocolate chip", "caesar dressing", "ranch"], ["milk chocolate"]),
        R(AllergenCode.Egg, AllergenStatus.Contains,
            ["egg", "egg yolk", "yolk", "egg white", "mayonnaise", "mayo", "meringue", "aioli", "custard", "brioche", "egg noodle", "hollandaise"],
            ["egg roll wrapper", "egg replacer", "flax egg", "chia egg"]),
        R(AllergenCode.Egg, AllergenStatus.MayContain, ["caesar dressing", "fresh pasta"]),
        R(AllergenCode.Fish, AllergenStatus.Contains,
            ["fish", "salmon", "tuna", "cod", "tilapia", "anchovy", "anchovies", "sardine", "halibut", "trout", "mackerel", "haddock",
             "catfish", "snapper", "swordfish", "pollock", "mahi mahi", "sea bass", "flounder", "fish sauce", "worcestershire", "bonito", "dashi"]),
        R(AllergenCode.Fish, AllergenStatus.MayContain, ["caesar dressing", "curry paste"]),
        R(AllergenCode.Shellfish, AllergenStatus.Contains, ["shrimp", "prawn", "crab", "lobster", "crawfish", "crayfish", "langoustine", "shrimp paste"]),
        R(AllergenCode.Shellfish, AllergenStatus.MayContain, ["curry paste", "oyster sauce"]),
        R(AllergenCode.TreeNuts, AllergenStatus.Contains,
            ["almond", "walnut", "pecan", "cashew", "pistachio", "hazelnut", "filbert", "macadamia", "brazil nut", "pine nut", "pignoli",
             "marzipan", "frangipane", "praline", "pesto", "nutella", "amaretti"]),
        R(AllergenCode.TreeNuts, AllergenStatus.MayContain, ["granola", "mixed nut", "nut"], ["peanut", "nutmeg", "coconut", "chestnut", "butternut", "nut free", "doughnut", "donut"]),
        R(AllergenCode.Peanuts, AllergenStatus.Contains, ["peanut", "peanut butter", "peanut oil", "groundnut", "satay"]),
        R(AllergenCode.Wheat, AllergenStatus.Contains, WheatWords, NotWheat),
        R(AllergenCode.Gluten, AllergenStatus.Contains, [.. WheatWords, "barley", "rye", "malt", "beer", "triticale", "pearl barley"], NotWheat),
        R(AllergenCode.Gluten, AllergenStatus.MayContain, ["oat", "oatmeal", "rolled oat", "granola", "broth", "stock", "bouillon"]),
        R(AllergenCode.Soy, AllergenStatus.Contains,
            ["soy", "soya", "soybean", "tofu", "edamame", "miso", "tempeh", "soy sauce", "tamari", "soymilk", "natto", "shoyu", "teriyaki", "hoisin"]),
        R(AllergenCode.Soy, AllergenStatus.MayContain, ["bouillon", "vegetable oil", "lecithin"]),
        R(AllergenCode.Sesame, AllergenStatus.Contains, ["sesame", "tahini", "benne", "gomasio", "za atar", "zaatar", "halva", "halvah"]),
    ];

    /// <summary>Words on a line that promise an allergen is left out.</summary>
    private static readonly (Regex Words, AllergenCode[] Removes)[] Suppressors =
    [
        (Words(["gluten free", "gf"]), [AllergenCode.Wheat, AllergenCode.Gluten]),
        (Words(["dairy free", "non dairy", "nondairy", "milk free"]), [AllergenCode.Milk]),
        (Words(["egg free", "eggless"]), [AllergenCode.Egg]),
        (Words(["vegan", "plant based"]), [AllergenCode.Milk, AllergenCode.Egg, AllergenCode.Fish, AllergenCode.Shellfish]),
        (Words(["nut free"]), [AllergenCode.TreeNuts, AllergenCode.Peanuts]),
        (Words(["soy free"]), [AllergenCode.Soy]),
    ];

    /// <summary>Lower case, accents and punctuation removed, hyphens as spaces ("half-and-half" → "half and half").</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return " " + string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
    }

    /// <summary>The allergens one ingredient (its line text and, if matched, its food's name) contains or may contain.</summary>
    public static IReadOnlyDictionary<AllergenCode, AllergenStatus> Detect(string? lineText, string? foodName = null)
    {
        var text = Normalize(lineText + " " + foodName);
        var found = new Dictionary<AllergenCode, AllergenStatus>();
        if (text.Trim().Length == 0)
        {
            return found;
        }

        try
        {
            foreach (var rule in Rules)
            {
                var checkText = rule.Exclude is null ? text : rule.Exclude.Replace(text, " ");
                if (!rule.Include.IsMatch(checkText))
                {
                    continue;
                }

                if (rule.Status == AllergenStatus.Contains || !found.ContainsKey(rule.Code))
                {
                    found[rule.Code] = rule.Status;
                }
            }

            foreach (var (words, removes) in Suppressors)
            {
                if (words.IsMatch(text))
                {
                    foreach (var code in removes)
                    {
                        found.Remove(code);
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // An absurd line: nothing detected for it rather than a hang.
        }

        return found;
    }

    /// <summary>Merges lines: Contains beats May contain.</summary>
    public static IReadOnlyDictionary<AllergenCode, AllergenStatus> ForRecipe(IEnumerable<(string Line, string? Food)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new Dictionary<AllergenCode, AllergenStatus>();
        foreach (var (line, food) in lines)
        {
            foreach (var (code, status) in Detect(line, food))
            {
                if (!result.TryGetValue(code, out var existing) || (existing == AllergenStatus.MayContain && status == AllergenStatus.Contains))
                {
                    result[code] = status;
                }
            }
        }

        return result;
    }

    /// <summary>BR-52: manual rows win; NotPresent (from either) leaves the allergen out.</summary>
    public static IReadOnlyDictionary<AllergenCode, AllergenStatus> Effective(IReadOnlyDictionary<AllergenCode, AllergenStatus> auto,
        IReadOnlyDictionary<AllergenCode, AllergenStatus> manual)
    {
        ArgumentNullException.ThrowIfNull(auto);
        ArgumentNullException.ThrowIfNull(manual);
        var result = new Dictionary<AllergenCode, AllergenStatus>(auto);
        foreach (var (code, status) in manual)
        {
            result[code] = status;
        }

        return result.Where(p => p.Value != AllergenStatus.NotPresent).ToFrozenDictionary();
    }
}
