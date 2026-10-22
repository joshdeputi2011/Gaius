using System.Net;
using System.Text.Json.Nodes;

namespace Gaius.Office;

internal sealed record OfficeTemplate(string Id, string Name, string Description);

internal static class OfficeTemplates
{
    public static IReadOnlyList<OfficeTemplate> ForKind(string kind) => kind switch {
        "document" => [new("blank", "Blank document", "Start with a clean page"), new("report", "Project report", "Summary, findings and next steps"), new("letter", "Business letter", "A clear, professional letter"), new("meeting", "Meeting notes", "Agenda, decisions and actions")],
        "spreadsheet" => [new("blank", "Blank workbook", "Start with an empty sheet"), new("budget", "Monthly budget", "Plan income and spending"), new("expenses", "Expense tracker", "Record and total your expenses"), new("project", "Project tracker", "Owners, dates and progress")],
        "presentation" => [new("blank", "Blank presentation", "Build your own story"), new("pitch", "Project pitch", "Problem, approach and impact"), new("lesson", "Lesson plan", "Objectives, examples and recap"), new("review", "Monthly review", "Highlights, results and next steps")],
        _ => []
    };

    public static OfficeFile Create(string kind, string id)
    {
        var template = ForKind(kind).FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException("Choose an available template.");
        var file = OfficeFile.Create(kind); if (id == "blank") return file;
        file.Title = template.Name;
        if (kind == "document") {
            string html = id switch {
                "report" => "<h1>Project report</h1><p>Project name · Prepared by · Date</p><h2>Executive summary</h2><p>Introduce the project and its most important outcome.</p><h2>Findings</h2><p>Describe your findings and supporting evidence.</p><h2>Next steps</h2><p>List the actions, owners and dates for the next stage.</p>",
                "letter" => "<h1>Your name</h1><p>Your address<br>Your email · Your phone</p><p>Date</p><p>Recipient name<br>Organisation<br>Address</p><p>Dear recipient,</p><p>Write your opening message here.</p><p>Add the details, context and any action you would like the recipient to take.</p><p>Kind regards,<br>Your name</p>",
                _ => "<h1>Meeting notes</h1><p>Meeting title · Date · Time</p><h2>Attendees</h2><p>Add the people attending.</p><h2>Agenda</h2><p>1. Opening discussion<br>2. Main topics<br>3. Next steps</p><h2>Decisions</h2><p>Record the decisions and their context.</p><h2>Action items</h2><p>Action · Owner · Due date</p>"
            };
            file.Content["html"] = html;
            var parsed = new HtmlAgilityPack.HtmlDocument(); parsed.LoadHtml(html);
            file.Content["text"] = string.Join("\n", parsed.DocumentNode.ChildNodes.Select(n => WebUtility.HtmlDecode(n.InnerText)));
        } else if (kind == "spreadsheet") {
            string[][] rows = id switch {
                "budget" => [["Monthly budget", "Planned", "Actual", "Difference"], ["Income", "3000", "3000", "=C2-B2"], ["Rent", "900", "900", "=C3-B3"], ["Groceries", "350", "320", "=C4-B4"], ["Transport", "150", "120", "=C5-B5"], ["Utilities", "180", "170", "=C6-B6"], ["Total expenses", "=SUM(B3:B6)", "=SUM(C3:C6)", "=C7-B7"], ["Remaining", "=B2-B7", "=C2-C7", "=C8-B8"]],
                "expenses" => [["Date", "Description", "Category", "Amount"], [DateTime.Today.ToString("yyyy-MM-dd"), "Sample expense", "Travel", "25"], ["", "", "Total", "=SUM(D2:D50)"]],
                _ => [["Task", "Owner", "Due date", "Status"], ["Define scope", "Your name", DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"), "In progress"], ["Build first draft", "", DateTime.Today.AddDays(14).ToString("yyyy-MM-dd"), "Not started"], ["Review and deliver", "", DateTime.Today.AddDays(21).ToString("yyyy-MM-dd"), "Not started"]]
            };
            var cells = (JsonObject)file.Sheet["cells"]!; var styles = new JsonObject();
            for (int r = 0; r < rows.Length; r++) for (int c = 0; c < rows[r].Length; c++) { string address = OfficeFile.Column(c) + (r + 1); cells[address] = rows[r][c]; if (r == 0) styles[address] = new JsonObject { ["bold"] = true, ["color"] = "#ffffff", ["background"] = "#107c41" }; }
            file.Sheet["styles"] = styles; file.Sheet["name"] = template.Name;
            file.Sheet["columnWidths"] = new JsonObject { ["A"] = 190, ["B"] = 160, ["C"] = 160, ["D"] = 160 };
        } else {
            (string Title, string Body)[] slides = id switch {
                "pitch" => [("Project pitch", "A clear idea. A meaningful outcome."), ("The opportunity", "Describe the problem and who it affects."), ("Our approach", "Explain your solution and what makes it useful."), ("Plan and impact", "Outline the milestones, resources and expected results.")],
                "lesson" => [("Today's lesson", "Topic · Class · Date"), ("Learning objectives", "By the end of this lesson, we will be able to…"), ("Explore and practise", "Introduce an example, question or activity."), ("What we learned", "Recap the key ideas and next steps.")],
                _ => [("Monthly review", "Team · Month · Year"), ("Highlights", "Share the progress that matters most."), ("Results", "Add the measures and insights behind the results."), ("Next month", "Set the priorities and actions for the coming month.")]
            };
            file.Slides.Clear();
            foreach (var (title, body) in slides) file.Slides.Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["title"] = title, ["body"] = body, ["background"] = "#fffaf7", ["objects"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["kind"] = "rectangle", ["x"] = 0, ["y"] = 94, ["width"] = 100, ["height"] = 6, ["color"] = "#c43e1c" }) });
        }
        return file;
    }
}
