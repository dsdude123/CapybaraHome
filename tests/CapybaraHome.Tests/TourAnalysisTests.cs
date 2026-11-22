using CapybaraHome.Application.Homes;
using CapybaraHome.Domain.Homes;

namespace CapybaraHome.Tests;

/// <summary>Covers what the model is told about a tour and what is taken from its answer.</summary>
public class TourAnalysisTests
{
    [Fact]
    public void The_material_lays_out_the_recorded_property_the_open_priorities_and_the_tour()
    {
        var property = Property() with
        {
            Address = "1 Main St",
            Rent = 2100m,
            ParkingFee = 150m,
            PetRent = 35.5m,
            Deposit = 500m,
            SquareFeet = 800,
            Internet = "Fibre",
            Utilities = [new PropertyUtilityView(1, UtilityKind.Electricity, null, 60m), new PropertyUtilityView(2, UtilityKind.Other, "Trash", 20m)],
            Amenities = [new PropertyAmenityView(1, "Gym", 0)],
            Points = [new PropertyPointView(1, PropertyPointKind.Pro, "Quiet street", 0), new PropertyPointView(2, PropertyPointKind.Con, "No lift", 0)],
            Priorities = [new PropertyPriorityView(7, "Dishwasher", 5m, true), new PropertyPriorityView(8, "Balcony", 5m, false)],
        };

        var material = TourAnalysis.Material(
            property,
            [new SearchPriorityView(9, "In-unit laundry", 5m, 0, 0)],
            "  Liked the light.  ",
            "We walked through the kitchen.",
            ["A bright kitchen.", "A small bathroom."],
            ["Called the office."],
            ["The lobby."]);

        Assert.Contains("Name: Harbor", material);
        Assert.Contains("Address: 1 Main St", material);
        Assert.Contains("Rent a month: $2100", material);
        Assert.Contains("Pet rent a month: $35.5", material);
        Assert.Contains("Square feet: 800", material);
        Assert.Contains("Utility: Electricity, $60 a month", material);
        Assert.Contains("Utility: Trash, $20 a month", material);
        Assert.Contains("Amenities:\n- Gym", material.ReplaceLineEndings("\n"));
        Assert.Contains("- Quiet street", material);
        Assert.Contains("- No lift", material);
        Assert.Contains("- Dishwasher: meets it", material);
        Assert.Contains("- Balcony: does not meet it", material);
        Assert.Contains("- Called the office.", material);
        Assert.Contains("- The lobby.", material);
        Assert.Contains("- id 9: In-unit laundry", material);
        Assert.Contains("Liked the light.", material);
        Assert.Contains("We walked through the kitchen.", material);
        Assert.Contains("1. A bright kitchen.", material);
        Assert.Contains("2. A small bathroom.", material);
    }

    [Fact]
    public void A_tour_with_nothing_in_a_part_says_so_rather_than_leaving_a_gap()
    {
        var material = TourAnalysis.Material(Property(), [], null, null, [], [], []).ReplaceLineEndings("\n");

        Assert.Contains("# Unmarked priorities to decide\nNone.", material);
        Assert.Contains("## Typed notes\nNone.", material);
        Assert.Contains("## Video transcript\nNone.", material);
        Assert.Contains("## Pictures\nNone.", material);
        Assert.DoesNotContain("Address:", material);
        Assert.DoesNotContain("Amenities:", material);
    }

    [Fact]
    public void Only_a_plain_yes_or_no_about_a_priority_that_was_asked_is_kept()
    {
        var findings = TourAnalysis.Parse(
            """
            {
              "summary": " A good place. ",
              "priorities": [
                { "id": 1, "decision": "yes", "evidence": "washer and dryer" },
                { "id": 2, "decision": " NO ", "evidence": "washer and dryer" },
                { "id": 3, "decision": "unclear", "evidence": "washer and dryer" },
                { "id": 4, "decision": "probably", "evidence": "washer and dryer" },
                { "id": "5", "decision": true, "evidence": "washer and dryer" },
                { "id": 6, "decision": false, "evidence": "washer and dryer" },
                { "id": 7, "decision": 1, "evidence": "washer and dryer" },
                { "id": 99, "decision": "yes", "evidence": "washer and dryer" },
                { "id": 8, "evidence": "washer and dryer" },
                { "decision": "yes", "evidence": "washer and dryer" },
                { "id": 1.5, "decision": "yes", "evidence": "washer and dryer" },
                { "id": null, "decision": "yes", "evidence": "washer and dryer" },
                "nonsense"
              ],
              "pros": ["Bright", "bright", "", 4, "  Near the park "],
              "cons": "none",
              "discrepancies": ["Rent is $2,300, not $2,100"],
            }
            """,
            [1, 2, 3, 4, 5, 6, 7, 8],
            "There is a washer and dryer in the unit.");

        Assert.Equal("A good place.", findings.Summary);
        Assert.Equal(
            new Dictionary<int, bool> { [1] = true, [2] = false, [5] = true, [6] = false },
            findings.Priorities);
        Assert.Equal(["Bright", "Near the park"], findings.Pros);
        Assert.Empty(findings.Cons);
        Assert.Equal(["Rent is $2,300, not $2,100"], findings.Discrepancies);
    }

    [Theory]
    [InlineData("\"There is a washer and dryer in the unit\"", true)]
    [InlineData("there is a WASHER, and dryer... in the unit!", true)]
    [InlineData("  No   balcony ", true)]
    [InlineData("The unit has laundry machines", false)]
    [InlineData("dryer", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_decision_counts_only_when_its_quote_is_really_in_the_tour(string? evidence, bool kept)
    {
        var entry = evidence is null
            ? """{ "id": 1, "decision": "yes" }"""
            : $$"""{ "id": 1, "decision": "yes", "evidence": {{System.Text.Json.JsonSerializer.Serialize(evidence)}} }""";

        var findings = TourAnalysis.Parse(
            $$"""{ "summary": "Fine.", "priorities": [ {{entry}}, { "id": 2, "decision": "no", "evidence": 7 } ] }""",
            [1, 2],
            "Okay. There is a washer and dryer in the unit, which is great.\nNo balcony.");

        Assert.Equal(kept, findings.Priorities.ContainsKey(1));
        Assert.DoesNotContain(2, findings.Priorities.Keys);
    }

    [Theory]
    [InlineData("<think>Let me see {not json}</think>\n{\"summary\":\"Fine\"}")]
    [InlineData("Here is the answer:\n```json\n{\"summary\":\"Fine\"}\n```\nHope that helps.")]
    [InlineData("```\n{\"summary\":\"Fine\"}\n```")]
    [InlineData("Sure. {\"summary\":\"Fine\"} Done.")]
    public void An_answer_wrapped_in_reasoning_a_fence_or_prose_is_still_read(string answer)
    {
        var findings = TourAnalysis.Parse(answer, [], null);

        Assert.Equal("Fine", findings.Summary);
        Assert.Empty(findings.Priorities);
        Assert.Empty(findings.Pros);
        Assert.Empty(findings.Discrepancies);
    }

    [Theory]
    [InlineData(null, "did not answer with the JSON")]
    [InlineData("I could not decide.", "did not answer with the JSON")]
    [InlineData("} backwards {", "did not answer with the JSON")]
    [InlineData("{ \"summary\": ", "did not answer with the JSON")]
    [InlineData("{ \"summary\": }", "could not be read")]
    [InlineData("{ \"pros\": [] }", "without a summary")]
    [InlineData("{ \"summary\": 4 }", "without a summary")]
    [InlineData("{ \"summary\": \"   \" }", "without a summary")]
    public void An_answer_that_is_not_what_was_asked_for_is_refused(string? answer, string expected)
    {
        var refused = Assert.Throws<PropertyTourException>(() => TourAnalysis.Parse(answer, [], null));

        Assert.Contains(expected, refused.Message);
    }

    [Fact]
    public void The_note_is_titled_and_carries_the_discrepancies_only_when_there_are_some()
    {
        var plain = TourAnalysis.Note(new TourFindings("All good.", new Dictionary<int, bool>(), [], [], []));
        var disputed = TourAnalysis.Note(new TourFindings(
            "All good.", new Dictionary<int, bool>(), [], [], ["Rent differs", "No gym"]));

        Assert.Equal("Tour Summary\n\nAll good.", plain);
        Assert.Equal("Tour Summary\n\nAll good.\n\nPossible Discrepancies\n\n- Rent differs\n- No gym", disputed);
    }

    [Fact]
    public void The_prompts_hold_the_rule_that_an_unclear_priority_is_left_alone()
    {
        Assert.Contains("\"unclear\"", TourAnalysis.SystemPrompt);
        Assert.Contains("Do not guess", TourAnalysis.SystemPrompt);
        Assert.Contains("description only", TourAnalysis.CaptionPrompt);
    }

    private static PropertyView Property() => new(
        1, "Harbor", null, false, null, null, null, null, null, null, null, null, null,
        [], [], [], [], [], [], [],
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
