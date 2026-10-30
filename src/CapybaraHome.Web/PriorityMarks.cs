namespace CapybaraHome.Web;

/// <summary>
/// Turns the three ways a property can stand against a priority into the words and the posted
/// values the UI uses. Not marked is its own answer: it is not the same as not meeting it.
/// </summary>
public static class PriorityMarks
{
    /// <summary>Every marking, in the order the UI offers them.</summary>
    public static readonly bool?[] All = [null, true, false];

    /// <summary>Returns the value <paramref name="mark"/> is posted as.</summary>
    public static string Value(bool? mark) => mark switch
    {
        true => "yes",
        false => "no",
        _ => "unmarked",
    };

    /// <summary>Returns the label for <paramref name="mark"/>.</summary>
    public static string Label(bool? mark) => mark switch
    {
        true => "Meets it",
        false => "Does not meet it",
        _ => "Not marked",
    };

    /// <summary>Returns the marking <paramref name="posted"/> stands for, unmarked for anything else.</summary>
    public static bool? Parse(string? posted) => posted switch
    {
        "yes" => true,
        "no" => false,
        _ => null,
    };
}
