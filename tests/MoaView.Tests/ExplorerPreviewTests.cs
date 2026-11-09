using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class ExplorerPreviewTests
{
    private static readonly ExplorerContext FirstView = new(100, 101);

    [Fact]
    public void SpaceGesture_TogglesOncePerPressAndConsumesMatchingReleaseAfterFocusChanges()
    {
        var gesture = new SpaceKeyGesture();
        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: true, eligible: true));
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: true, eligible: false));
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: false, eligible: false));
        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: false, eligible: true));
    }

    [Fact]
    public void SpaceGesture_AnIneligiblePressStaysUntouchedEvenWhenFocusChangesWhileHeld()
    {
        var gesture = new SpaceKeyGesture();
        Assert.Equal(SpaceKeyAction.PassThrough, gesture.Handle(isDown: true, eligible: false));
        Assert.Equal(SpaceKeyAction.PassThrough, gesture.Handle(isDown: true, eligible: true));
        Assert.Equal(SpaceKeyAction.PassThrough, gesture.Handle(isDown: false, eligible: true));
        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
    }

    [Fact]
    public async Task Coordinator_OpensRefreshesWithoutReloadingAndClosesWithSpace()
    {
        var selection = new SelectionStub { Path = @"C:\images\first.png" };
        var presenter = new PresenterStub();
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => FirstView);

        await coordinator.ToggleAsync(FirstView);
        Assert.True(presenter.IsOpen);
        Assert.Equal(selection.Path, presenter.Path);
        await coordinator.RefreshAsync();
        Assert.Equal(1, presenter.ShowCount);

        selection.Path = @"C:\images\second.jpg";
        await coordinator.RefreshAsync();
        Assert.Equal(2, presenter.ShowCount);
        Assert.Equal(selection.Path, presenter.Path);

        await coordinator.ToggleAsync(FirstView);
        Assert.False(presenter.IsOpen);
        await coordinator.RefreshAsync();
        Assert.Equal(3, selection.ReadCount);
    }

    [Fact]
    public async Task Coordinator_DoesNotRestoreOriginalSelectionWhenPlaylistAdvancesInPreview()
    {
        const string explorerSelection = @"C:\music\track01.mp3";
        const string nextTrack = @"C:\music\track02.mp3";
        var selection = new SelectionStub { Path = explorerSelection };
        var presenter = new PresenterStub();
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => FirstView);

        await coordinator.ToggleAsync(FirstView);
        Assert.Equal(explorerSelection, presenter.Path);

        // The music player advances its MediaElement without another Shell selection change.
        presenter.AdvanceTrack(nextTrack);
        await coordinator.RefreshAsync();

        Assert.Equal(nextTrack, presenter.Path);
        Assert.Equal(1, presenter.ShowCount);
    }

    [Fact]
    public async Task Coordinator_EmptyOrMultipleSelectionDoesNotOpenAndClearsExistingPreview()
    {
        var selection = new SelectionStub();
        var presenter = new PresenterStub();
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => FirstView);
        await coordinator.ToggleAsync(FirstView);
        Assert.False(presenter.IsOpen);
        selection.Path = @"C:\photo.png";
        await coordinator.ToggleAsync(FirstView);
        Assert.True(presenter.IsOpen);
        selection.Path = null;
        await coordinator.RefreshAsync();
        Assert.False(presenter.IsOpen);
    }

    [Fact]
    public async Task Coordinator_CancelledOpeningCannotReopenAfterSlowShellReply()
    {
        var pending = new TaskCompletionSource<string?>();
        var selection = new SelectionStub { Pending = pending.Task };
        var presenter = new PresenterStub();
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => FirstView);

        var opening = coordinator.ToggleAsync(FirstView);
        await coordinator.ToggleAsync(FirstView); // The second Space cancels the pending open.
        await coordinator.ToggleAsync(FirstView); // Do not queue another COM call behind it.
        Assert.Equal(1, selection.ReadCount);
        pending.SetResult(@"C:\slow.png");
        await opening;
        Assert.False(presenter.IsOpen);

        selection.Pending = null;
        selection.Path = @"C:\next.png";
        await coordinator.ToggleAsync(FirstView);
        Assert.True(presenter.IsOpen);
        Assert.Equal(selection.Path, presenter.Path);
    }

    [Fact]
    public async Task Coordinator_DiscardsReplyWhenActiveExplorerTabChanges()
    {
        var pending = new TaskCompletionSource<string?>();
        var selection = new SelectionStub { Pending = pending.Task };
        var presenter = new PresenterStub();
        ExplorerContext? current = FirstView;
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => current);
        var opening = coordinator.ToggleAsync(FirstView);
        current = new ExplorerContext(FirstView.WindowHandle, 102);
        pending.SetResult(@"C:\wrong-tab.png");
        await opening;
        Assert.False(presenter.IsOpen);
        selection.Pending = null;
        selection.Path = @"C:\active-tab.png";
        await coordinator.ToggleAsync(current.Value);
        Assert.Equal(selection.Path, presenter.Path);
    }

    [Fact]
    public async Task Coordinator_FollowsTabsButDoesNotReadOtherAppsOrAnotherExplorerWindow()
    {
        var selection = new SelectionStub { Path = @"C:\one.png" };
        var presenter = new PresenterStub();
        ExplorerContext? current = FirstView;
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => current);
        await coordinator.ToggleAsync(FirstView);
        current = null;
        await coordinator.RefreshAsync();
        Assert.Equal(1, selection.ReadCount);
        Assert.True(presenter.IsOpen);
        current = new ExplorerContext(100, 102);
        selection.Path = @"C:\two.png";
        await coordinator.RefreshAsync();
        Assert.Equal(current.Value, presenter.Context);
        Assert.Equal(selection.Path, presenter.Path);
        current = new ExplorerContext(200, 201);
        await coordinator.RefreshAsync();
        Assert.False(presenter.IsOpen);
        Assert.Equal(2, selection.ReadCount);
    }

    [Fact]
    public async Task Coordinator_CloseDuringRefreshAndDisposeInvalidatePendingReplies()
    {
        var selection = new SelectionStub { Path = @"C:\one.png" };
        var presenter = new PresenterStub();
        var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => FirstView);
        await coordinator.ToggleAsync(FirstView);
        var pending = new TaskCompletionSource<string?>();
        selection.Pending = pending.Task;
        var refreshing = coordinator.RefreshAsync();
        await coordinator.RefreshAsync();
        Assert.Equal(2, selection.ReadCount);
        coordinator.Dispose();
        pending.SetResult(@"C:\two.png");
        await refreshing;
        await coordinator.ToggleAsync(FirstView);
        Assert.False(presenter.IsOpen);
        Assert.Equal(1, presenter.ShowCount);
    }

    [Fact]
    public void Presenter_ShowsActualImagesAndErrorStatesAndSpaceClosesOnlyPeekWindow()
    {
        using var temp = new TempTestDirectory();
        var first = SampleImageFactory.WriteValidImage(temp.GetPath("first.png"), ImageFormatKind.Png, 72, 48);
        var second = SampleImageFactory.WriteValidImage(temp.GetPath("second.bmp"), ImageFormatKind.Bmp, 40, 80);
        var extensionless = temp.GetPath("notes");
        File.WriteAllText(extensionless, "탐색기에서 보는 텍스트");
        var damaged = temp.GetPath("damaged.png");
        File.WriteAllText(damaged, "broken image");

        StaTestRunner.Run(() =>
        {
            var presenter = new ExplorerPreviewPresenter();
            var normalWindow = new MainWindow();
            try
            {
                normalWindow.ShowActivated = false;
                normalWindow.ShowInTaskbar = false;
                normalWindow.Show();
                var context = new ExplorerContext(new System.Windows.Interop.WindowInteropHelper(normalWindow).Handle, 0);
                presenter.Show(first, context);
                var peek = presenter.Window!;
                Render(peek);
                Assert.True(peek.IsExplorerPreview);
                Assert.True(peek.VerifyRenderedState());
                Assert.False(peek.ShowActivated);
                Assert.False(peek.ShowInTaskbar);
                Assert.False(peek.Topmost);
                Assert.Equal(context.WindowHandle, new System.Windows.Interop.WindowInteropHelper(peek).Owner);
                Assert.Equal(72, peek.ViewModel.DisplayPixelWidth);

                presenter.Show(second, context);
                Render(peek);
                Assert.Same(peek, presenter.Window);
                Assert.Equal(80, peek.ViewModel.DisplayPixelHeight);
                Assert.True(peek.VerifyRenderedState());

                presenter.Show(extensionless, context);
                Render(peek);
                Assert.Equal(PreviewStateKind.TextLoaded, peek.ViewModel.State);
                Assert.Contains("탐색기에서 보는 텍스트", peek.ViewModel.TextContent);
                Assert.True(peek.VerifyRenderedState());
                presenter.Show(damaged, context);
                Render(peek);
                Assert.Equal(PreviewStateKind.Error, peek.ViewModel.State);
                Assert.Null(peek.ViewModel.PreviewBitmap);
                Assert.True(peek.VerifyRenderedState());

                Assert.False(peek.HandleKeyInput(Key.Space, ModifierKeys.Control));
                Assert.True(peek.HandleKeyInput(Key.Space));
                Assert.False(presenter.IsOpen);
                Assert.Null(presenter.Window);
                Assert.True(normalWindow.IsVisible);
                Assert.False(normalWindow.HandleKeyInput(Key.Space));

                presenter.Show(first, context);
                Render(presenter.Window!);
                Assert.True(presenter.Window!.VerifyRenderedState());
                Assert.True(presenter.Window.HandleKeyInput(Key.Escape));
                Assert.False(presenter.IsOpen);
            }
            finally
            {
                presenter.Close();
                normalWindow.Close();
            }
        });
    }

    [Fact]
    public void Integration_StartStopAndRestartReleasesNativeHookAndSessionLock()
    {
        StaTestRunner.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using (var integration = new ExplorerPreviewIntegration(dispatcher))
            {
                // The named mutex is thread-owned. A second STA must not install another hook.
                StaTestRunner.Run(() =>
                {
                    var error = Assert.Throws<InvalidOperationException>(() =>
                        new ExplorerPreviewIntegration(Dispatcher.CurrentDispatcher));
                    Assert.Contains("다른 모아뷰", error.Message);
                });
                integration.Dispose();
                integration.Dispose();
            }

            // Exercise re-enabling after the hook thread and the selection worker shut down.
            using var restarted = new ExplorerPreviewIntegration(dispatcher);
        });
    }

    [Fact]
    public async Task SelectionService_UnknownExplorerWindowReturnsNoFileAndCanBeDisposed()
    {
        using var service = new WindowsExplorerSelectionService();
        Assert.Null(await service.GetSelectedPathAsync(new ExplorerContext(-1, -1)).WaitAsync(TimeSpan.FromSeconds(10)));
        service.Dispose();
        Assert.Null(await service.GetSelectedPathAsync(FirstView));
    }

    private static void Render(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private sealed class SelectionStub : IExplorerSelectionService
    {
        public string? Path { get; set; }
        public Task<string?>? Pending { get; set; }
        public int ReadCount { get; private set; }
        public Task<string?> GetSelectedPathAsync(ExplorerContext context)
        {
            ReadCount++;
            return Pending ?? Task.FromResult(Path);
        }
    }

    private sealed class PresenterStub : IExplorerPreviewPresenter
    {
        public bool IsOpen { get; private set; }
        public string? Path { get; private set; }
        public ExplorerContext Context { get; private set; }
        public int ShowCount { get; private set; }
        public void Show(string path, ExplorerContext context)
        {
            IsOpen = true;
            Path = path;
            Context = context;
            ShowCount++;
        }

        public void AdvanceTrack(string path) => Path = path;

        public void Close() => IsOpen = false;
    }
}
