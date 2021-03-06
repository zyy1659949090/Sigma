﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using Sigma.Core.Handlers;
using Sigma.Core.MathAbstract;
using System;
using Sigma.Core.Utils;

namespace Sigma.Core.Training.Initialisers
{
	/// <summary>
	/// A constant value initialiser, which initialises ndarrays with one constant value.
	/// </summary>
	public class ConstantValueInitialiser : IInitialiser
	{
		/// <summary>
		/// The registry containing relevant parameters and information about this initialiser.
		/// </summary>
		public IRegistry Registry { get; } = new Registry(tags: "initialiser");

		/// <summary>
		/// Create a constant value initialiser for a certain constant value.
		/// </summary>
		/// <param name="constantValue">The constant value.</param>
		public ConstantValueInitialiser(double constantValue)
		{
			Registry.Set("constant_value", constantValue, typeof(double));
		}

		/// <summary>
		/// Create a constant value initialiser for a certain constant value.
		/// </summary>
		/// <param name="constantValue">The constant value.</param>
		/// <returns>A constant value initialiser with the given constant value.</returns>
		public ConstantValueInitialiser Constant(double constantValue)
		{
			return new ConstantValueInitialiser(constantValue);
		}

		public void Initialise(INDArray array, IComputationHandler handler, Random random)
		{
			if (array == null) throw new ArgumentNullException(nameof(array));
			if (handler == null) throw new ArgumentNullException(nameof(handler));
			if (random == null) throw new ArgumentNullException(nameof(random));

			handler.Fill(Registry.Get<double>("constant_value"), array);
		}

		public void Initialise(INumber number, IComputationHandler handler, Random random)
		{
			if (number == null) throw new ArgumentNullException(nameof(number));
			if (handler == null) throw new ArgumentNullException(nameof(handler));
			if (random == null) throw new ArgumentNullException(nameof(random));

			number.Value = Registry.Get<double>("constant_value");
		}
	}
}
