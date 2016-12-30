using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Sigma.Core.Monitors.WPF.Model.UI.Resources;
using Sigma.Core.Utils;

namespace Sigma.Core.Monitors.WPF.View.Factories.Defaults.StatusBar
{
	public class StatusBarFactory : IUIFactory<UIElement>
	{
		public const string RegistryIdentifier = "statusbar_registry";

		public const string LegendFactoryIdentifier = "statusbar_legend_factory";
		public const string TaskVisualizerFactoryIdentifier = "taskvisualizer_factory";
		public const string CustomFactoryIdentifier = "custom_factory";


		private readonly GridLength[] _gridLengths;

		private readonly double _height;

		private readonly int _customColumn;
		private readonly int _taskColumn;
		private readonly int _legendColumn;

		/// <summary>
		/// 
		/// </summary>
		/// <param name="parentRegistry"></param>
		/// <param name="height"></param>
		/// <param name="gridLengths"></param>
		public StatusBarFactory(IRegistry parentRegistry, double height, params GridLength[] gridLengths) : this(parentRegistry, height, 0, 1, 2, gridLengths)
		{

		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="parentRegistry"></param>
		/// <param name="height"></param>
		/// <param name="customColumn"></param>
		/// <param name="taskColumn">The index for the task visualizer to use. If negativ, no task visualizer will be added. </param>
		/// <param name="legendColumn">The index for the column to use. If negativ, no legend will be added. </param>
		/// <param name="gridLengths"></param>
		public StatusBarFactory(IRegistry parentRegistry, double height, int customColumn, int taskColumn, int legendColumn, params GridLength[] gridLengths)
		{
			if (parentRegistry == null || !parentRegistry.ContainsKey(RegistryIdentifier))
			{
				Debug.WriteLine("new one");
				Registry = new Registry(parentRegistry);
				parentRegistry?.Add(RegistryIdentifier, Registry);
			}
			else
			{
				Debug.WriteLine("Existing one");
				Registry = (IRegistry) parentRegistry[RegistryIdentifier];
			}

			if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

			if (gridLengths.Length == 0)
				throw new ArgumentException(@"Value cannot be an empty collection.", nameof(gridLengths));

			CheckColumn(customColumn, gridLengths.Length);
			CheckColumn(taskColumn, gridLengths.Length);
			CheckColumn(legendColumn, gridLengths.Length);

			_height = height;

			_customColumn = customColumn;
			_taskColumn = taskColumn;
			_legendColumn = legendColumn;

			_gridLengths = gridLengths;
		}

		/// <summary>
		///		This method checks whether the passed column can be placed in length. Throw an exception otherwise. 
		/// </summary>
		/// <param name="column"></param>
		/// <param name="length"></param>
		// ReSharper disable once UnusedParameter.Local
		private void CheckColumn(int column, int length)
		{
			if (column >= length)
			{
				throw new ArgumentOutOfRangeException(nameof(column));
			}
		}

		/// <summary>
		/// 
		/// </summary>
		public IRegistry Registry { get; set; }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="app"></param>
		/// <param name="window"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		public UIElement CreateElement(Application app, Window window, params object[] parameters)
		{
			IUIFactory<UIElement> customFactory = EnsureRegistry(CustomFactoryIdentifier, null);
			IUIFactory<UIElement> taskVisualizerFactory = EnsureRegistry(TaskVisualizerFactoryIdentifier, () => new TaskVisualizerFactory(3));
			IUIFactory<UIElement> legendFactory = EnsureRegistry(LegendFactoryIdentifier, () => new StatusBarLegendFactory());

			Grid statusBarGrid = new Grid
			{
				Height = _height,
				Background = UIResources.WindowTitleColorBrush
			};

			// Add a column for every specified column
			foreach (GridLength gridLength in _gridLengths)
			{
				ColumnDefinition newColumn = new ColumnDefinition
				{
					Width = new GridLength(gridLength.Value, gridLength.GridUnitType),
				};

				statusBarGrid.ColumnDefinitions.Add(newColumn);
			}

			if (_customColumn >= 0 && customFactory != null)
			{
				AddCustom(app, window, statusBarGrid, customFactory, parameters);
			}

			if (_taskColumn >= 0)
			{
				AddTaskVisualizer(app, window, statusBarGrid, taskVisualizerFactory, parameters);
			}
			if (_legendColumn >= 0)
			{
				AddLegends(app, window, statusBarGrid, legendFactory, parameters);
			}


			return statusBarGrid;
		}


		protected void AddGenericFactory(Application app, Window window, Grid grid, IUIFactory<UIElement> factory, int column, IEnumerable<object> parameters)
		{
			UIElement taskVisualizer = factory.CreateElement(app, window, parameters);

			grid.Children.Add(taskVisualizer);
			Grid.SetColumn(taskVisualizer, column);
		}


		protected void AddCustom(Application app, Window window, Grid grid, IUIFactory<UIElement> factory, object[] parameters)
		{
			AddGenericFactory(app, window, grid, factory, _customColumn, parameters);
		}


		private IUIFactory<UIElement> EnsureRegistry(string identifier, Func<IUIFactory<UIElement>> createFactory)
		{
			IUIFactory<UIElement> factory;

			if (!Registry.TryGetValue(identifier, out factory) && createFactory != null)
			{
				factory = createFactory();
				Registry.Add(identifier, factory);
			}

			return factory;
		}

		protected void AddTaskVisualizer(Application app, Window window, Grid grid, IUIFactory<UIElement> factory, IEnumerable<object> parameters)
		{
			AddGenericFactory(app, window, grid, factory, _taskColumn, parameters);
		}

		protected void AddLegends(Application app, Window window, Grid grid, IUIFactory<UIElement> factory, IEnumerable<object> parameters)
		{
			StackPanel stackPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

			foreach (object legendInfo in parameters)
			{
				UIElement element = factory.CreateElement(app, window, legendInfo);
				stackPanel.Children.Add(element);
			}

			grid.Children.Add(stackPanel);
			Grid.SetColumn(stackPanel, _legendColumn);
		}
	}
}