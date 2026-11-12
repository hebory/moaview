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
    public async Task Coordinator_SpaceOnAnotherExplorerImmediatelyOpensThatSelection()
    {
        var selection = new SelectionStub { Path = @"C:\one.png" };
        var presenter = new PresenterStub();
        ExplorerContext? current = FirstView;
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => current);

        await coordinator.ToggleAsync(FirstView);
        var otherWindow = new ExplorerContext(200, 201);
        current = otherWindow;
        selection.Path = @"C:\two.png";

        await coordinator.ToggleAsync(otherWindow);

        Assert.True(presenter.IsOpen);
        Assert.Equal(selection.Path, presenter.Path);
        Assert.Equal(otherWindow, presenter.Context);
        Assert.Equal(2, presenter.ShowCount);
    }

    [Fact]
    public async Task Coordinator_OtherExplorerSpaceWaitsForOldReadAndDiscardsItsLateReply()
    {
        var pending = new TaskCompletionSource<string?>();
        var selection = new SelectionStub { Pending = pending.Task };
        var presenter = new PresenterStub();
        ExplorerContext? current = FirstView;
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => current);

        var oldOpening = coordinator.ToggleAsync(FirstView);
        var otherWindow = new ExplorerContext(200, 201);
        current = otherWindow;
        selection.Pending = null;
        selection.Path = @"C:\two.png";

        await coordinator.ToggleAsync(otherWindow);
        pending.SetResult(@"C:\stale.png");
        await oldOpening;

        Assert.Equal(selection.Path, presenter.Path);
        Assert.Equal(otherWindow, presenter.Context);
        Assert.Equal(1, presenter.ShowCount);
        Assert.Equal(2, selection.ReadCount);
    }

    [Fact]
    public async Task Coordinator_SpaceOnAnotherExplorerWithNoSelectionKeepsPreviewClosed()
    {
        var selection = new SelectionStub { Path = @"C:\one.png" };
        var presenter = new PresenterStub();
        ExplorerContext? current = FirstView;
        using var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => current);

        await coordinator.ToggleAsync(FirstView);
        var otherWindow = new ExplorerContext(200, 201);
        current = otherWindow;
        selection.Path = null;

        await coordinator.ToggleAsync(otherWindow);

        Assert.False(presenter.IsOpen);
        Assert.Equal(1, presenter.ShowCount);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("dispose")]
    [InlineData("focus-changed")]
    public async Task Coordinator_CancelledPendingExplorerSwitchDoesNotReopenOrQuery(string cancellation)
    {
        var pending = new TaskCompletionSource<string?>();
        var selection = new SelectionStub { Pending = pending.Task };
        var presenter = new PresenterStub();
        ExplorerContext? current = FirstView;
        var coordinator = new ExplorerPreviewCoordinator(selection, presenter, () => current);

        var oldOpening = coordinator.ToggleAsync(FirstView);
        var otherWindow = new ExplorerContext(200, 201);
        current = otherWindow;
        await coordinator.ToggleAsync(otherWindow);
        Assert.Equal(1, selection.ReadCount);

        if (cancellation == "close")
        {
            coordinator.Close();
        }
        else if (cancellation == "dispose")
        {
            coordinator.Dispose();
        }
        else
        {
            current = new ExplorerContext(300, 301);
        }

        selection.Pending = null;
        selection.Path = @"C:\should-not-open.png";
        pending.SetResult(@"C:\stale.png");
        await oldOpening;

        Assert.False(presenter.IsOpen);
        Assert.Equal(0, presenter.ShowCount);
        Assert.Equal(1, selection.ReadCount);
        coordinator.Dispose();
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
        Assert.Equal(@"Local\MoaView.ExplorerSpacePreview", ExplorerPreviewIntegration.DefaultMutexName);
        var mutexName = $@"{ExplorerPreviewIntegration.DefaultMutexName}.Test.{Guid.NewGuid():N}";
        StaTestRunner.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using (var integration = new ExplorerPreviewIntegration(dispatcher, mutexName))
            {
                // The named mutex is thread-owned. A second STA must not install another hook.
                StaTestRunner.Run(() =>
                {
                    var error = Assert.Throws<InvalidOperationException>(() =>
                        new ExplorerPreviewIntegration(Dispatcher.CurrentDispatcher, mutexName));
                    Assert.Contains("다른 모아뷰", error.Message);
                });
                integration.Dispose();
                integration.Dispose();
            }

            // Exercise re-enabling after the hook thread and the selection worker shut down.
            using var restarted = new ExplorerPreviewIntegration(dispatcher, mutexName);
        });
    }

    [Fact]
    public void SpaceGesture_LongPressConsumesTypematicRepeatsAndRecoversLostKeyUpOnlyAfterQuietPeriod()
    {
        var gesture = new SpaceKeyGesture();
        Assert.False(gesture.IsPressed);

        // Initial Space press in Explorer toggles once.
        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
        Assert.True(gesture.IsPressed);

        // Sustained typematic repeats while holding Space must be consumed without re-toggling.
        for (var repeat = 0; repeat < 25; repeat++)
        {
            Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: true, eligible: false));
            Assert.True(gesture.IsPressed);
        }

        // Maintenance timer must not reset while the key is physically down or while repeats are recent.
        Assert.False(gesture.RecoverReleasedKey(physicallyDown: true, TimeSpan.FromSeconds(5)));
        Assert.False(gesture.RecoverReleasedKey(physicallyDown: false, TimeSpan.FromMilliseconds(1400)));
        Assert.True(gesture.IsPressed);

        // Normal key-up consumes the release and allows the next long-press to toggle closed once.
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: false, eligible: true));
        Assert.False(gesture.IsPressed);

        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
        for (var repeat = 0; repeat < 15; repeat++)
        {
            Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: true, eligible: false));
        }

        // If a key-up was lost (e.g. hook replaced or desktop switched), recovery resets after 1500ms quiet period.
        Assert.True(gesture.RecoverReleasedKey(physicallyDown: false, TimeSpan.FromMilliseconds(1500)));
        Assert.False(gesture.IsPressed);
        Assert.Equal(SpaceKeyAction.Toggle, gesture.Handle(isDown: true, eligible: true));
        Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: false, eligible: true));
    }

    [Fact]
    public void HookAndPresenter_LongPressOpensAndClosesRealPreviewWindowOnceWhilePeriodicRenewalRuns()
    {
        using var temp = new TempTestDirectory();
        var samplePng = SampleImageFactory.WriteValidImage(temp.GetPath("long-press.png"), ImageFormatKind.Png, 64, 48);

        StaTestRunner.Run(() =>
        {
            var diagnostics = new List<string>();
            var presenter = new ExplorerPreviewPresenter();
            var explorerWindow = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            ExplorerSpaceKeyHook? hook = null;
            try
            {
                explorerWindow.Show();
                var explorerHandle = new System.Windows.Interop.WindowInteropHelper(explorerWindow).Handle;
                var context = new ExplorerContext(explorerHandle, 101);
                var selection = new SelectionStub { Path = samplePng };
                using var coordinator = new ExplorerPreviewCoordinator(
                    selection,
                    presenter,
                    () => context,
                    ExplorerWindowContext.IsWindow);

                hook = new ExplorerSpaceKeyHook(
                    source => CompleteSynchronously(coordinator.ToggleAsync(source)),
                    TimeSpan.FromMilliseconds(60),
                    message =>
                    {
                        lock (diagnostics)
                        {
                            diagnostics.Add(message);
                        }
                    });

                Assert.NotEqual(0, hook.RegistrationHandle);
                Thread.Sleep(180);
                Assert.True(hook.RegistrationCount >= 2, $"Expected periodic hook renewal, got RegistrationCount={hook.RegistrationCount}");
                Assert.NotEqual(0, hook.RegistrationHandle);

                // Long-press #1 (1 initial down + 15 typematic repeat downs + 1 up) opens the real WPF preview window once.
                var gesture = new SpaceKeyGesture();
                for (var step = 0; step < 16; step++)
                {
                    var captured = !gesture.IsPressed ? context : (ExplorerContext?)null;
                    var action = gesture.Handle(isDown: true, captured is not null);
                    if (action == SpaceKeyAction.Toggle && captured is { } target)
                    {
                        CompleteSynchronously(coordinator.ToggleAsync(target));
                    }
                }

                Render(presenter.Window!);
                Assert.True(presenter.IsOpen);
                Assert.Equal(1, selection.ReadCount);
                Assert.True(presenter.Window!.VerifyRenderedState());
                Assert.Equal(64, presenter.Window.ViewModel.DisplayPixelWidth);
                Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: false, eligible: true));
                Assert.True(presenter.IsOpen);

                // Long-press #2 (1 initial down + 15 typematic repeat downs + 1 up) closes the preview window once.
                for (var step = 0; step < 16; step++)
                {
                    var captured = !gesture.IsPressed ? context : (ExplorerContext?)null;
                    var action = gesture.Handle(isDown: true, captured is not null);
                    if (action == SpaceKeyAction.Toggle && captured is { } target)
                    {
                        CompleteSynchronously(coordinator.ToggleAsync(target));
                    }
                }

                Assert.Equal(SpaceKeyAction.Consume, gesture.Handle(isDown: false, eligible: true));
                Assert.False(presenter.IsOpen);
                Assert.Null(presenter.Window);
                Assert.Equal(1, selection.ReadCount);
            }
            finally
            {
                hook?.Dispose();
                presenter.Close();
                explorerWindow.Close();
            }
        });
    }

    [Fact]
    public void PresenterAndCoordinator_TabSwitchInSameExplorerWindowUpdatesRenderedPreviewAndImmediateSpaceSwitchesTab()
    {
        using var temp = new TempTestDirectory();
        var tab1Image = SampleImageFactory.WriteValidImage(temp.GetPath("tab1.png"), ImageFormatKind.Png, 72, 48);
        var tab2Image = SampleImageFactory.WriteValidImage(temp.GetPath("tab2.bmp"), ImageFormatKind.Bmp, 40, 80);
        var tab3Zip = ContentPreviewTests.WriteZip(
            temp.GetPath("tab3.zip"),
            new Dictionary<string, byte[]>
            {
                ["note.txt"] = System.Text.Encoding.UTF8.GetBytes("탭 전환 ZIP 내부 텍스트"),
                ["inner.png"] = File.ReadAllBytes(tab1Image),
            });

        StaTestRunner.Run(() =>
        {
            var presenter = new ExplorerPreviewPresenter();
            var explorerWindow = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                explorerWindow.Show();
                var explorerHwnd = new System.Windows.Interop.WindowInteropHelper(explorerWindow).Handle;
                var tab1 = new ExplorerContext(explorerHwnd, 101);
                var tab2 = new ExplorerContext(explorerHwnd, 102);
                var tab3 = new ExplorerContext(explorerHwnd, 103);
                var tab4SamePath = new ExplorerContext(explorerHwnd, 104);
                var tab5Empty = new ExplorerContext(explorerHwnd, 105);

                ExplorerContext? activeContext = tab1;
                var tabSelections = new Dictionary<ExplorerContext, string?>
                {
                    [tab1] = tab1Image,
                    [tab2] = tab2Image,
                    [tab3] = tab3Zip,
                    [tab4SamePath] = tab3Zip,
                    [tab5Empty] = null,
                };
                var selection = new ContextSelectionStub(ctx => tabSelections.GetValueOrDefault(ctx));
                using var coordinator = new ExplorerPreviewCoordinator(
                    selection,
                    presenter,
                    () => activeContext,
                    ExplorerWindowContext.IsWindow);

                // 1. Open Space preview on Tab 1
                CompleteSynchronously(coordinator.ToggleAsync(tab1));
                Assert.True(presenter.IsOpen);
                var peek = presenter.Window!;
                Render(peek);
                Assert.Equal(explorerHwnd, new System.Windows.Interop.WindowInteropHelper(peek).Owner);
                Assert.Equal(72, peek.ViewModel.DisplayPixelWidth);
                Assert.True(peek.VerifyRenderedState());

                // 2. Switch to Tab 2 and press Space immediately (before 200ms RefreshAsync): opens Tab 2 selection
                activeContext = tab2;
                CompleteSynchronously(coordinator.ToggleAsync(tab2));
                Assert.True(presenter.IsOpen);
                peek = presenter.Window!;
                Render(peek);
                Assert.Equal(explorerHwnd, new System.Windows.Interop.WindowInteropHelper(peek).Owner);
                Assert.Equal(40, peek.ViewModel.DisplayPixelWidth);
                Assert.Equal(80, peek.ViewModel.DisplayPixelHeight);
                Assert.True(peek.VerifyRenderedState());

                // 3. Switch to Tab 3 and let periodic RefreshAsync update the open preview window in-place to ZIP
                activeContext = tab3;
                CompleteSynchronously(coordinator.RefreshAsync());
                Assert.True(presenter.IsOpen);
                Assert.Same(peek, presenter.Window);
                Render(peek);
                Assert.Equal(PreviewStateKind.ArchiveLoaded, peek.ViewModel.State);
                Assert.True(peek.VerifyRenderedState());

                // 4. Switch to Tab 4 (which has the same file path selected as Tab 3); RefreshAsync updates active tab source so Space closes it
                activeContext = tab4SamePath;
                CompleteSynchronously(coordinator.RefreshAsync());
                Assert.True(presenter.IsOpen);
                CompleteSynchronously(coordinator.ToggleAsync(tab4SamePath));
                Assert.False(presenter.IsOpen);
                Assert.Null(presenter.Window);

                // 5. Reopen on Tab 1, then switch to Tab 5 (no selection): RefreshAsync closes the preview
                activeContext = tab1;
                CompleteSynchronously(coordinator.ToggleAsync(tab1));
                Assert.True(presenter.IsOpen);
                Render(presenter.Window!);
                Assert.True(presenter.Window!.VerifyRenderedState());

                activeContext = tab5Empty;
                CompleteSynchronously(coordinator.RefreshAsync());
                Assert.False(presenter.IsOpen);
                Assert.Null(presenter.Window);
            }
            finally
            {
                presenter.Close();
                explorerWindow.Close();
            }
        });
    }

    [Fact]
    public void PresenterAndCoordinator_ExplorerWindowCloseCleansUpPreviewAndRestartedExplorerOpensNewPreview()
    {
        using var temp = new TempTestDirectory();
        var firstImage = SampleImageFactory.WriteValidImage(temp.GetPath("before-restart.png"), ImageFormatKind.Png, 64, 32);
        var secondImage = SampleImageFactory.WriteValidImage(temp.GetPath("after-restart.bmp"), ImageFormatKind.Bmp, 48, 96);

        StaTestRunner.Run(() =>
        {
            var presenter = new ExplorerPreviewPresenter();
            var explorerWindow1 = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            var explorerWindow2 = new MainWindow { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                explorerWindow1.Show();
                var hwnd1 = new System.Windows.Interop.WindowInteropHelper(explorerWindow1).Handle;
                Assert.True(ExplorerWindowContext.IsWindow(hwnd1));
                var context1 = new ExplorerContext(hwnd1, 101);

                ExplorerContext? activeContext = context1;
                var selection = new SelectionStub { Path = firstImage };
                using var coordinator = new ExplorerPreviewCoordinator(
                    selection,
                    presenter,
                    () => activeContext,
                    ExplorerWindowContext.IsWindow);

                // Open preview owned by the first Explorer window
                CompleteSynchronously(coordinator.ToggleAsync(context1));
                Assert.True(presenter.IsOpen);
                Render(presenter.Window!);
                Assert.Equal(hwnd1, new System.Windows.Interop.WindowInteropHelper(presenter.Window!).Owner);
                Assert.Equal(64, presenter.Window!.ViewModel.DisplayPixelWidth);
                Assert.True(presenter.Window.VerifyRenderedState());

                // Close the first Explorer window and move focus outside Explorer (activeContext = null)
                activeContext = null;
                explorerWindow1.Close();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.False(ExplorerWindowContext.IsWindow(hwnd1));

                // RefreshAsync cleans up any previous preview window and coordinator state
                CompleteSynchronously(coordinator.RefreshAsync());
                Assert.False(presenter.IsOpen);
                Assert.Null(presenter.Window);

                // Start a new Explorer window and press Space to open a new preview
                explorerWindow2.Show();
                var hwnd2 = new System.Windows.Interop.WindowInteropHelper(explorerWindow2).Handle;
                Assert.True(ExplorerWindowContext.IsWindow(hwnd2));
                Assert.NotEqual(hwnd1, hwnd2);
                var context2 = new ExplorerContext(hwnd2, 202);
                activeContext = context2;
                selection.Path = secondImage;

                CompleteSynchronously(coordinator.ToggleAsync(context2));
                Assert.True(presenter.IsOpen);
                Render(presenter.Window!);
                Assert.Equal(hwnd2, new System.Windows.Interop.WindowInteropHelper(presenter.Window!).Owner);
                Assert.Equal(48, presenter.Window!.ViewModel.DisplayPixelWidth);
                Assert.Equal(96, presenter.Window.ViewModel.DisplayPixelHeight);
                Assert.True(presenter.Window.VerifyRenderedState());
            }
            finally
            {
                presenter.Close();
                explorerWindow1.Close();
                explorerWindow2.Close();
            }
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

    private static void CompleteSynchronously(Task task) => task.GetAwaiter().GetResult();

    private sealed class ContextSelectionStub(Func<ExplorerContext, string?> resolver) : IExplorerSelectionService
    {
        public Task<string?> GetSelectedPathAsync(ExplorerContext context) => Task.FromResult(resolver(context));
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
