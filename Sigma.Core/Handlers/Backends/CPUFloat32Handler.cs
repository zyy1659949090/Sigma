﻿/* 
MIT License

Copyright (c) 2016 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using Sigma.Core.Data;
using Sigma.Core.MathAbstract;
using System;

namespace Sigma.Core.Handlers.Backends
{
	/// <summary>
	/// A computation handler that runs computations on the CPU with 32-bit floating point precision. 
	/// </summary>
	public class CpuFloat32Handler : IComputationHandler
	{
		public IDataType DataType => DataTypes.Float32;

		public INDArray Create(params long[] shape)
		{
			return new NDArray<float>(shape: shape);
		}

		public void InitAfterDeserialisation(INDArray array)
		{
			// nothing to do here for this handler, all relevant components are serialised automatically
		}

		public long GetSizeBytes(params INDArray[] arrays)
		{
			long totalSizeBytes = 0L;

			foreach (INDArray array in arrays)
			{
				long sizeBytes = 52L; // let's just assume 52bytes of base fluff, I really have no idea

				sizeBytes += array.Length * DataType.SizeBytes;
				sizeBytes += (array.Shape.Length) * 8L * 2;

				totalSizeBytes += sizeBytes;
			}

			return totalSizeBytes;
		}

		public bool IsInterchangeable(IComputationHandler otherHandler)
		{
			//there are no interchangeable implementations so it will have to be the same type 
			return otherHandler.GetType() == GetType();
		}

		public bool CanConvert(INDArray array, IComputationHandler otherHandler)
		{
			//if it's the same base unit and at least the same precision we can convert
			return otherHandler.DataType.BaseUnderlyingType == DataType.BaseUnderlyingType && otherHandler.DataType.SizeBytes >= DataType.SizeBytes;
		}

		public INDArray Convert(INDArray array, IComputationHandler otherHandler)
		{
			return new NDArray<float>(array.GetDataAs<float>(), array.Shape);
		}

		public void Fill(INDArray filler, INDArray arrayToFill)
		{
			IDataBuffer<float> arrayToFillData = ((NDArray<float>) arrayToFill).Data;
			IDataBuffer<float> fillerData = ((NDArray<float>) filler).Data;

			arrayToFillData.Data.FillWith(fillerData.Data, 0, 0, Math.Min(arrayToFill.Length, filler.Length));
		}

		public void Fill<TOther>(TOther value, INDArray arrayToFill)
		{
			IDataBuffer<float> arrayToFillData = ((NDArray<float>) arrayToFill).Data;

			float floatValue = (float) System.Convert.ChangeType(value, typeof(float));

			for (int i = 0; i < arrayToFillData.Length; i++)
			{
				arrayToFillData.Data.SetValue(floatValue, i);
			}
		}

		public void Add<TOther>(INDArray array, TOther value, INDArray output)
		{
			IDataBuffer<float> arrayData = ((NDArray<float>) array).Data;
			IDataBuffer<float> outputData = ((NDArray<float>) output).Data;

			float floatValue = (float) System.Convert.ChangeType(value, typeof(float));

			for (long i = 0; i < arrayData.Length; i++)
			{
				outputData.SetValue(arrayData.GetValue(i) + floatValue, i);
			}
		}

		public void Subtract<TOther>(INDArray array, TOther value, INDArray output)
		{
			IDataBuffer<float> arrayData = ((NDArray<float>) array).Data;
			IDataBuffer<float> outputData = ((NDArray<float>) output).Data;

			float floatValue = (float) System.Convert.ChangeType(value, typeof(float));

			for (long i = 0; i < arrayData.Length; i++)
			{
				outputData.SetValue(arrayData.GetValue(i) - floatValue, i);
			}
		}

		public void Multiply<TOther>(INDArray array, TOther value, INDArray output)
		{
			IDataBuffer<float> arrayData = ((NDArray<float>) array).Data;
			IDataBuffer<float> outputData = ((NDArray<float>) output).Data;

			float floatValue = (float) System.Convert.ChangeType(value, typeof(float));

			for (long i = 0; i < arrayData.Length; i++)
			{
				outputData.SetValue(arrayData.GetValue(i) * floatValue, i);
			}
		}

		public void Divide<TOther>(INDArray array, TOther value, INDArray output)
		{
			IDataBuffer<float> arrayData = ((NDArray<float>) array).Data;
			IDataBuffer<float> outputData = ((NDArray<float>) output).Data;

			float floatValue = (float) System.Convert.ChangeType(value, typeof(float));

			for (long i = 0; i < arrayData.Length; i++)
			{
				outputData.SetValue(arrayData.GetValue(i) / floatValue, i);
			}
		}
	}
}
