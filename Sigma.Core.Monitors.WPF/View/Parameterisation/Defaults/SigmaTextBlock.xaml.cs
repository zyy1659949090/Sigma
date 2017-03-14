﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

namespace Sigma.Core.Monitors.WPF.View.Parameterisation.Defaults
{
	/// <summary>
	/// Sigmas way of displaying objects. 
	/// </summary>
	[ParameterVisualiser(typeof(object), Priority = VisualiserPriority.Lower)]
	public partial class SigmaTextBlock
	{
		/// <summary>
		/// The text that is visualised. 
		/// </summary>
		public string Text { get; set; }

		/// <summary>
		/// Determines whether the parameter is editable or not. 
		/// </summary>
		public override bool IsReadOnly { get; set; } = true;

		/// <summary>
		/// The fully resolved key to access the synchandler.
		/// </summary>
		public override string Key { get; set; }

		/// <summary>
		/// Create a new default textblock that can display parameters (i.e. objects).
		/// </summary>
		public SigmaTextBlock()
		{
			InitializeComponent();

			DataContext = this;
		}
	}
}