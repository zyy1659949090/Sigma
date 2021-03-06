/* 
MIT License

Copyright (c) 2016-2017 Florian C�sar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.Controls;
using Sigma.Core.Monitors.WPF.Model.UI.Resources;

namespace Sigma.Core.Monitors.WPF.ViewModel.TitleBar
{
	/// <summary>
	/// The class that manages all <see cref="TitleBarItem"/>s.
	/// 
	/// It is the menu in the top next to the application icon. 
	/// </summary>
	public class TitleBarControl : WindowCommands, IEnumerable<TitleBarItem>
	{
		/// <summary>
		///     The children of the <see cref="TitleBarControl" />.
		/// </summary>
		private readonly Dictionary<string, TitleBarItem> _children;

		public TitleBarControl()
		{
			_children = new Dictionary<string, TitleBarItem>();

			Menu = new Menu();

			Items.Add(Menu);

			//Styling options
			//TODO: style-file?
			ShowLastSeparator = false;
		}

		public Menu Menu { get; }

		/// <summary>
		///     Get a child at the specified index.
		/// </summary>
		/// <param name="str">The specified index. </param>
		/// <returns></returns>
		public TitleBarItem this[string str] => _children[str];

		public IEnumerator<TitleBarItem> GetEnumerator()
		{
			return _children.Values.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return _children.Values.GetEnumerator();
		}

		/// <summary>
		///     Add a <see cref="TitleBarItem" /> to the <see cref="TitleBarControl" />.
		///     Do not use <see cref="ItemCollection.Add" /> or Menu.Items.Add. (Although it will be called internally)
		/// </summary>
		/// <param name="window"></param>
		/// <param name="item">The item to add.</param>
		/// <param name="app"></param>
		public void AddItem(Application app, Window window, TitleBarItem item)
		{
			AddItem(app, window, item, UIResources.IdealForegroundColorBrush);
		}

		/// <summary>
		///     Add a <see cref="TitleBarItem" /> to the <see cref="TitleBarControl" />.
		///     Do not use <see cref="ItemCollection.Add" /> ore Menu.Items.Add. (Although it will be called internally)
		/// </summary>
		/// <param name="window"></param>
		/// <param name="item">The item to add.</param>
		/// <param name="foregroundBrush">The foreground colour for the newly created item.</param>
		/// <param name="app"></param>
		public void AddItem(Application app, Window window, TitleBarItem item, Brush foregroundBrush)
		{
			AddItem(app, window, item, item.ToString(), foregroundBrush);
		}

		/// <summary>
		///     Add a <see cref="TitleBarItem" /> to the <see cref="TitleBarControl" />.
		///     Do not use <see cref="ItemCollection.Add" /> ore Menu.Items.Add. (Although it will be called internally)
		/// </summary>
		/// <param name="window"></param>
		/// <param name="item">The item to add.</param>
		/// <param name="key">The key that can later be found in the children</param>
		/// <param name="foregroundBrush">The foreground colour for the newly created item.</param>
		/// <param name="app"></param>
		public void AddItem(Application app, Window window, TitleBarItem item, string key, Brush foregroundBrush)
		{
			if (foregroundBrush == null)
			{
				throw new ArgumentNullException(nameof(foregroundBrush));
			}

			Menu.Items.Add(item.Content);

			item.App = app;
			item.Window = window;
			_children.Add(key, item);

			item.Content.Foreground = foregroundBrush;
		}

		/// <summary>
		///     Remove a <see cref="TitleBarItem" /> from the <see cref="TitleBarControl" />.
		/// </summary>
		/// <param name="item"></param>
		public void RemoveItem(TitleBarItem item)
		{
			Menu.Items.Remove(item.Content);
			_children.Remove(item.ToString());
		}
	}
}