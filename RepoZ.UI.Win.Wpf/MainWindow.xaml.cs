﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using RepoZ.Api.Common.Git;
using RepoZ.Api.Git;
using RepoZ.Api.Win.Git;
using TinyIoC;

namespace RepoZ.UI.Win.Wpf
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : MetroWindow
	{
		private IRepositoryActionProvider _repositoryActionProvider;

		private DefaultRepositoryMonitor _monitor;

		public MainWindow(StatusCharacterMap statusCharacterMap,
			IRepositoryInformationAggregator aggregator,
			IRepositoryMonitor repositoryMonitor,
			IRepositoryActionProvider repositoryActionProvider)
		{
			InitializeComponent();

			_monitor = repositoryMonitor as DefaultRepositoryMonitor;
			if (_monitor != null)
			{
				_monitor.OnScanStateChanged += OnScanStateChanged;
				ShowScanningState(_monitor.Scanning);
			}

			_repositoryActionProvider = repositoryActionProvider;

			lstRepositories.ItemsSource = aggregator.Repositories;
			var view = CollectionViewSource.GetDefaultView(lstRepositories.ItemsSource);
			view.CollectionChanged += View_CollectionChanged;
			view.Filter = FilterRepositories;

			lstRepositories.Items.SortDescriptions.Add(
				new SortDescription(nameof(RepositoryView.Name),
				ListSortDirection.Ascending));

			txtHelp.Text = GetHelp(statusCharacterMap);

			PlaceFormToLowerRight();
		}

		private void View_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			// use the list's itemsource directly, this one is not filtered (otherwise searching in the UI without matches could lead to the "no repositories yet"-screen)
			var hasRepositories = lstRepositories.ItemsSource.OfType<RepositoryView>().Any();
			dockMain.Visibility = hasRepositories ? Visibility.Visible : Visibility.Hidden;
			tbNoRepositories.Visibility = hasRepositories ? Visibility.Hidden : Visibility.Visible;
		}

		protected override void OnActivated(EventArgs e)
		{
			base.OnActivated(e);
			ShowUpdateIfAvailable();
			txtFilter.Focus();
		}

		protected override void OnDeactivated(EventArgs e)
		{
			base.OnDeactivated(e);
			Hide();
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			e.Cancel = true;
			Hide();
		}

		protected override void OnPreviewKeyDown(KeyEventArgs e)
		{
			base.OnPreviewKeyDown(e);

			if (e.Key == Key.Escape)
			{
				bool isFilterActive = txtFilter.IsFocused && !string.IsNullOrEmpty(txtFilter.Text);
				if (!isFilterActive)
					Hide();
			}
		}

		public void ShowAndActivate()
		{
			Dispatcher.Invoke(() =>
			{
				if (!IsShown)
					Show();

				Activate();
				txtFilter.Focus();
			});
		}

		private void OnScanStateChanged(object sender, bool isScanning)
		{
			Dispatcher.Invoke(() => ShowScanningState(isScanning));
		}

		private void lstRepositories_MouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			InvokeActionOnCurrentRepository();
		}

		private void lstRepositories_ContextMenuOpening(object sender, ContextMenuEventArgs e)
		{
			var selectedViews = lstRepositories.SelectedItems?.OfType<RepositoryView>();

			if (selectedViews == null || !selectedViews.Any())
			{
				e.Handled = true;
				return;
			}

			var items = ((FrameworkElement)e.Source).ContextMenu.Items;
			items.Clear();

			var innerRepositories = selectedViews.Select(view => view.Repository);
			foreach (var action in _repositoryActionProvider.GetContextMenuActions(innerRepositories))
			{
				if (action.BeginGroup && items.Count > 0)
					items.Add(new Separator());

				items.Add(CreateMenuItem(sender, action, selectedViews));
			}
		}

		private void lstRepositories_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Return || e.Key == Key.Enter)
				InvokeActionOnCurrentRepository();
		}

		private void InvokeActionOnCurrentRepository()
		{
			var selectedView = lstRepositories.SelectedItem as RepositoryView;
			if (selectedView == null || !selectedView.WasFound)
				return;

			RepositoryAction action;

			if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.LeftCtrl))
				action = _repositoryActionProvider.GetSecondaryAction(selectedView.Repository);
			else
				action = _repositoryActionProvider.GetPrimaryAction(selectedView.Repository);

			action?.Action?.Invoke(this, new EventArgs());
		}

		private void HelpButton_Click(object sender, RoutedEventArgs e)
		{
			transitionerMain.SelectedIndex = (transitionerMain.SelectedIndex == 0 ? 1 : 0);
		}

		private void ScanButton_Click(object sender, RoutedEventArgs e)
		{
			_monitor.ScanForLocalRepositoriesAsync();
		}

		private void UpdateButton_Click(object sender, RoutedEventArgs e)
		{
			bool hasLink = !string.IsNullOrWhiteSpace(App.AvailableUpdate?.Url);
			if (hasLink)
				Process.Start(App.AvailableUpdate.Url);
		}

		private void PlaceFormToLowerRight()
		{
			Top = SystemParameters.WorkArea.Height - Height - 1;
			Left = SystemParameters.WorkArea.Width - Width - 1;
		}

		private void ShowUpdateIfAvailable()
		{
			UpdateButton.Visibility = App.AvailableUpdate == null ? Visibility.Hidden : Visibility.Visible;
			UpdateButton.ToolTip = App.AvailableUpdate == null ? "" : $"Version {App.AvailableUpdate.VersionString} is available.";
			UpdateButton.Tag = App.AvailableUpdate;
		}

		private MenuItem CreateMenuItem(object sender, RepositoryAction action, IEnumerable<RepositoryView> affectedViews = null)
		{
			Action<object, object> clickAction = (object clickSender, object clickArgs) =>
			{
				if (action?.Action != null)
				{
					var coords = new float[] { 0, 0 };

					// run actions in the UI async to not block it
					if (action.ExecutionCausesSynchronizing)
					{
						Task.Run(() => SetViewsSynchronizing(affectedViews, true))
							.ContinueWith(t => action.Action(null, coords))
							.ContinueWith(t => SetViewsSynchronizing(affectedViews, false));
					}
					else
					{
						Task.Run(() => action.Action(null, coords));
					}
				}
			};

			var item = new MenuItem()
			{
				Header = action.Name,
				IsEnabled = action.CanExecute
			};
			item.Click += new RoutedEventHandler(clickAction);

			if (action.SubActions != null)
			{
				foreach (var subAction in action.SubActions)
					item.Items.Add(CreateMenuItem(sender, subAction));
			}

			return item;
		}

		private void SetViewsSynchronizing(IEnumerable<RepositoryView> affectedViews, bool synchronizing)
		{
			if (affectedViews == null)
				return;

			foreach (var view in affectedViews)
				view.IsSynchronizing = synchronizing;
		}

		private void ShowScanningState(bool isScanning)
		{
			ScanButton.IsEnabled = !isScanning;
			ScanButton.Content = isScanning ? "Scanning ..." : "⚡ Scan computer";
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);

			if (e.Key == Key.F && Keyboard.IsKeyDown(Key.LeftCtrl))
			{
				txtFilter.Focus();
				txtFilter.SelectAll();
			}

			if (e.Key == Key.Down && txtFilter.IsFocused)
				lstRepositories.Focus();
		}

		private void txtFilter_TextChanged(object sender, TextChangedEventArgs e)
		{
			CollectionViewSource.GetDefaultView(lstRepositories.ItemsSource).Refresh();
		}

		private bool FilterRepositories(object item)
		{
			if (String.IsNullOrEmpty(txtFilter.Text))
				return true;
			else
				return (item as RepositoryView).Name.IndexOf(txtFilter.Text, StringComparison.OrdinalIgnoreCase) > -1;
		}

		private void txtFilter_Finish(object sender, EventArgs e)
		{
			lstRepositories.Focus();
			if (lstRepositories.Items.Count > 0)
				lstRepositories.SelectedIndex = 0;
		}

		private string GetHelp(StatusCharacterMap statusCharacterMap)
		{
			return $@"
RepoZ is showing all git repositories found on local drives. Each repository is listed with a status string which could look like this:

    current_branch  [i]   +1   ~2   -3   |   +4   ~5   -6 


current_branch
The current branch or the SHA of a detached HEAD.

[i] Represents the branch status in relation to its remote (tracked origin) branch.

[i] =  {statusCharacterMap.IdenticalSign}
The local branch is at the same commit level as the remote branch.

[i] =  {statusCharacterMap.ArrowUpSign}<num>
The local branch is ahead of the remote branch by the specified number of commits; a 'git push' is required to update the remote branch.

[i] =  {statusCharacterMap.ArrowDownSign}<num>
The local branch is behind the remote branch by the specified number of commits; a 'git pull' is required to update the local branch.

[i] =  {statusCharacterMap.NoUpstreamSign}
The local branch has no upstream branch. 'git push' needs to be called with '--set-upstream' to push changes to a remote branch.

The following numbers represent added (+1), modified (~2) and removed files (-3) from the index.
The numbers after the pipe sign represent added (+4), modified (~5) and removed files (-6) on the working directory.

Please note:
This information reflects the state of the remote tracked branch after the last git fetch/pull of the remote.
Note that the status might be shorter if possible to improve readablility.

";
		}

		public bool IsShown => Visibility == Visibility.Visible && IsActive;
	}
}
