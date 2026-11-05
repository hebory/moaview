using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MoaView.Models;
using Block = System.Windows.Documents.Block;
using MdTable = Markdig.Extensions.Tables;

namespace MoaView.Controls;

/// <summary>Native, selectable Markdown view. No HTML, scripts, URLs or local images are loaded.</summary>
public sealed class MarkdownPreviewControl : FlowDocumentScrollViewer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().Build();
    private const int MaxRenderedCharacters = 100_000;
    private const int MaxElements = 5000;
    private int _elements;

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(nameof(Markdown), typeof(string),
        typeof(MarkdownPreviewControl), new PropertyMetadata(string.Empty, (d, _) => ((MarkdownPreviewControl)d).Render()));
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(nameof(Theme), typeof(MarkdownThemeOption),
        typeof(MarkdownPreviewControl), new PropertyMetadata(MarkdownThemeOption.All[0], (d, _) => ((MarkdownPreviewControl)d).ApplyTheme()));

    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    public MarkdownThemeOption Theme { get => (MarkdownThemeOption)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }

    public MarkdownPreviewControl()
    {
        IsToolBarVisible = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        BorderThickness = new Thickness(1);
        Render();
    }

    private void Render()
    {
        var text = Markdown ?? string.Empty;
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic"), FontSize = 15,
            PagePadding = new Thickness(24), ColumnWidth = double.PositiveInfinity,
        };
        _elements = 0;
        try
        {
            var source = text.Length > MaxRenderedCharacters ? text[..MaxRenderedCharacters] : text;
            var syntax = Markdig.Markdown.Parse(source, Pipeline);
            AddBlocks(document.Blocks, syntax, 0);
            if (text.Length > MaxRenderedCharacters)
                document.Blocks.Add(new Paragraph(new Run("서식 미리보기는 10만 자까지 표시합니다. 나머지는 원문 보기에서 확인하세요.")));
        }
        catch (Exception ex) when (ex is MarkdownRenderLimitException or ArgumentException or InvalidOperationException)
        {
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph(new Run("문서 구조가 복잡해 서식 미리보기를 표시할 수 없습니다. 원문 보기에서 확인하세요.")));
        }
        Document = document;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var theme = Theme ?? MarkdownThemeOption.All[0];
        Background = theme.Background;
        Foreground = theme.Foreground;
        BorderBrush = theme.Border;
        if (Document is not { } document) return;
        document.Background = theme.Background;
        document.Foreground = theme.Foreground;
        document.Resources["CodeBackground"] = theme.CodeBackground;
        document.Resources["Muted"] = theme.Muted;
        document.Resources["Accent"] = theme.Accent;
        document.Resources["Rule"] = theme.Border;
        // Resource updates preserve the document, selection and reading position.
    }

    private void CheckBudget(int depth)
    {
        if (depth > 32 || ++_elements > MaxElements) throw new MarkdownRenderLimitException();
    }

    private void AddBlocks(BlockCollection destination, ContainerBlock source, int depth)
    {
        CheckBudget(depth);
        foreach (var block in source)
        {
            CheckBudget(depth);
            switch (block)
            {
                case HeadingBlock heading:
                    var title = Paragraph(heading.Inline, depth);
                    title.FontSize = heading.Level switch { 1 => 30, 2 => 25, 3 => 21, 4 => 18, _ => 16 };
                    title.FontWeight = FontWeights.SemiBold;
                    title.Margin = new Thickness(0, 18, 0, 10);
                    destination.Add(title);
                    break;
                case ParagraphBlock paragraph:
                    destination.Add(Paragraph(paragraph.Inline, depth));
                    break;
                case CodeBlock code:
                    var codeText = new Paragraph(new Run(code.Lines.ToString()))
                    {
                        FontFamily = new FontFamily("Consolas, Malgun Gothic"), FontSize = 13,
                        Padding = new Thickness(12), Margin = new Thickness(0, 6, 0, 12),
                    };
                    codeText.SetResourceReference(TextElement.BackgroundProperty, "CodeBackground");
                    destination.Add(codeText);
                    break;
                case QuoteBlock quote:
                    var section = new Section { BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(14, 0, 0, 0), Margin = new Thickness(0, 6, 0, 12) };
                    section.SetResourceReference(Block.BorderBrushProperty, "Rule");
                    section.SetResourceReference(TextElement.ForegroundProperty, "Muted");
                    AddBlocks(section.Blocks, quote, depth + 1);
                    destination.Add(section);
                    break;
                case ListBlock list:
                    var items = new System.Windows.Documents.List { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Padding = new Thickness(24, 0, 0, 0), Margin = new Thickness(0, 0, 0, 12) };
                    if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start)) items.StartIndex = Math.Clamp(start, 1, 1_000_000);
                    foreach (var child in list.OfType<ListItemBlock>())
                    {
                        var item = new ListItem();
                        AddBlocks(item.Blocks, child, depth + 1);
                        items.ListItems.Add(item);
                    }
                    destination.Add(items);
                    break;
                case MdTable.Table table:
                    destination.Add(BuildTable(table, depth + 1));
                    break;
                case ThematicBreakBlock:
                    var rule = new Paragraph { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 10, 0, 14), FontSize = 1 };
                    rule.SetResourceReference(Block.BorderBrushProperty, "Rule");
                    destination.Add(rule);
                    break;
                case ContainerBlock container:
                    AddBlocks(destination, container, depth + 1);
                    break;
            }
        }
    }

    private Paragraph Paragraph(ContainerInline? inline, int depth)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10), LineHeight = 23 };
        if (inline is not null) AddInlines(paragraph.Inlines, inline, depth + 1);
        return paragraph;
    }

    private void AddInlines(InlineCollection destination, ContainerInline source, int depth)
    {
        CheckBudget(depth);
        for (var inline = source.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            CheckBudget(depth);
            switch (inline)
            {
                case LiteralInline literal: destination.Add(new Run(literal.Content.ToString())); break;
                case HtmlEntityInline entity: destination.Add(new Run(entity.Transcoded.ToString())); break;
                case AutolinkInline autolink:
                    var address = new Run(autolink.Url) { TextDecorations = TextDecorations.Underline };
                    address.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                    destination.Add(address);
                    break;
                case CodeInline code:
                    var run = new Run(code.Content) { FontFamily = new FontFamily("Consolas, Malgun Gothic") };
                    run.SetResourceReference(TextElement.BackgroundProperty, "CodeBackground");
                    destination.Add(run);
                    break;
                case LineBreakInline line:
                    destination.Add(line.IsHard ? new LineBreak() : new Run(" "));
                    break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    AddInlines(span.Inlines, emphasis, depth + 1);
                    destination.Add(span);
                    break;
                case LinkInline link:
                    // Display labels and destinations without navigating or fetching resources.
                    var label = new Span { ToolTip = link.Url };
                    label.SetResourceReference(TextElement.ForegroundProperty, "Accent");
                    if (link.IsImage) label.Inlines.Add(new Run("[이미지: "));
                    AddInlines(label.Inlines, link, depth + 1);
                    if (link.IsImage) label.Inlines.Add(new Run("]"));
                    else label.TextDecorations = TextDecorations.Underline;
                    destination.Add(label);
                    break;
                case ContainerInline container: AddInlines(destination, container, depth + 1); break;
            }
        }
    }

    private System.Windows.Documents.Table BuildTable(MdTable.Table source, int depth)
    {
        CheckBudget(depth);
        var table = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 14) };
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        foreach (var sourceRow in source.OfType<MdTable.TableRow>())
        {
            CheckBudget(depth);
            var row = new System.Windows.Documents.TableRow();
            if (sourceRow.IsHeader)
            {
                row.FontWeight = FontWeights.SemiBold;
                row.SetResourceReference(TextElement.BackgroundProperty, "CodeBackground");
            }
            foreach (var sourceCell in sourceRow.OfType<MdTable.TableCell>())
            {
                var cell = new System.Windows.Documents.TableCell { BorderThickness = new Thickness(0.5), Padding = new Thickness(8),
                    ColumnSpan = Math.Clamp(sourceCell.ColumnSpan, 1, 100) };
                cell.SetResourceReference(System.Windows.Documents.TableCell.BorderBrushProperty, "Rule");
                AddBlocks(cell.Blocks, sourceCell, depth + 1);
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        return table;
    }

    private sealed class MarkdownRenderLimitException : Exception;
}
