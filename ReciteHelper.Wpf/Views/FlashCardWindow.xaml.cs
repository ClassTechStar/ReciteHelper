using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Services;
using System.Windows;

namespace ReciteHelper.Wpf.Views;

/// <summary>
/// Flashcard study mode: flips through a project's questions card by card and
/// records whether each card was known, persisting the results to the project.
/// </summary>
public partial class FlashCardWindow : Window
{
    private readonly Project _project;
    private readonly IProjectFileService _projectFileService;
    private readonly List<Question> _cards;
    private int _currentIndex;
    private int _knownCount;
    private int _unknownCount;
    private bool _revealed;
    private bool _saved;

    public FlashCardWindow(Project project, IProjectFileService projectFileService)
    {
        _project = project;
        _projectFileService = projectFileService;
        _cards = (project.Chapters ?? [])
            .SelectMany(chapter => chapter.Questions ?? [])
            .Where(question => !string.IsNullOrWhiteSpace(question.Text))
            .ToList();

        InitializeComponent();
        Loaded += async (_, _) => await ShowCurrentCardAsync();
    }

    private async Task ShowCurrentCardAsync()
    {
        if (_currentIndex >= _cards.Count)
        {
            await FinishAsync();
            return;
        }

        var question = _cards[_currentIndex];
        QuestionTypeText.Text = question.TypeDisplayName;
        QuestionText.Text = question.IsFillBlank
            ? FillBlankTextNormalizer.NormalizeForDisplay(
                question.Text ?? string.Empty,
                question.GetCorrectAnswers())
            : question.Text;
        AnswerText.Text = question.GetCorrectAnswerText();
        AnswerText.Visibility = Visibility.Collapsed;
        _revealed = false;

        RevealButton.Visibility = Visibility.Visible;
        KnowButton.Visibility = Visibility.Collapsed;
        DontKnowButton.Visibility = Visibility.Collapsed;
        ProgressText.Text = $"第 {_currentIndex + 1} / {_cards.Count} 张 · 已认识 {_knownCount} · 不认识 {_unknownCount}";
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        if (_revealed)
            return;

        AnswerText.Visibility = Visibility.Visible;
        _revealed = true;
        RevealButton.Visibility = Visibility.Collapsed;
        KnowButton.Visibility = Visibility.Visible;
        DontKnowButton.Visibility = Visibility.Visible;
    }

    private async void KnowButton_Click(object sender, RoutedEventArgs e)
    {
        _knownCount++;
        RecordStatus(true);
        await ShowCurrentCardAsync();
    }

    private async void DontKnowButton_Click(object sender, RoutedEventArgs e)
    {
        _unknownCount++;
        RecordStatus(false);
        await ShowCurrentCardAsync();
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowCurrentCardAsync();
    }

    private void RecordStatus(bool known)
    {
        _cards[_currentIndex].Status = known;
        _currentIndex++;
    }

    private async Task FinishAsync()
    {
        if (!_saved)
        {
            _saved = true;
            try
            {
                await _projectFileService.SaveProjectAsync(_project);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存学习记录失败：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        MessageBox.Show(
            $"卡片学习完成。\n认识 {_knownCount} 张，不认识 {_unknownCount} 张，学习记录已保存。",
            "完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Close();
    }
}
