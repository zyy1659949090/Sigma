﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using System;
using System.Numerics;
using DiffSharp.Backend;
using Microsoft.FSharp.Core;
using Sigma.Core.MathAbstract;
using Sigma.Core.Utils;
using static DiffSharp.Util;

namespace Sigma.Core.Handlers.Backends.SigmaDiff
{
	/// <summary>
	/// A DiffSharp backend handle for 32-bit floats as passed to the underlying DiffSharp implementation.
	/// </summary>
	public unsafe class DiffSharpFloat32BackendHandle : DiffSharpBackendHandle<float>
	{
		public IBlasBackend BlasBackend { get; }
		public ILapackBackend LapackBackend { get; }

		/// <summary>
		/// Create a DiffSharpFloat32BackendHandle with a certain BLAS and LAPACK backend and an associated handle tag. 
		/// </summary>
		/// <param name="blasBackend">The BLAS backend to use (must use 32-bit floats).</param>
		/// <param name="lapackBackend">The LAPACK backend to use (must use 32-bit floats).</param>
		/// <param name="backendTag">The backend tag to use.</param>
		public DiffSharpFloat32BackendHandle(IBlasBackend blasBackend, ILapackBackend lapackBackend, long backendTag) : base(backendTag)
		{
			if (blasBackend == null) throw new ArgumentNullException(nameof(blasBackend));
			if (lapackBackend == null) throw new ArgumentNullException(nameof(lapackBackend));

			BlasBackend = blasBackend;
			LapackBackend = lapackBackend;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.CreateDataBuffer"/>
		public override ISigmaDiffDataBuffer<float> CreateDataBuffer(float[] values)
		{
			return new SigmaDiffDataBuffer<float>(values, backendTag: BackendTag);
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.L1Norm_V"/>
		public override float L1Norm_V(ISigmaDiffDataBuffer<float> value)
		{
			if (value.Length == 0)
			{
				return 0.0f;
			}

			fixed (float* aref = &value.Data[value.Offset])
			{
				int len = value.Length;
				int inca = 1;

				return BlasBackend.Sasum(&len, aref, &inca);
			}
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.L2Norm_V"/>
		public override float L2Norm_V(ISigmaDiffDataBuffer<float> value)
		{
			if (value.Length == 0)
			{
				return 0.0f;
			}

			fixed (float* aref = &value.Data[value.Offset])
			{
				int len = value.Length;
				int inca = 1;

				return BlasBackend.Snrm2(&len, aref, &inca);
			}
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.SupNorm_V"/>
		public override float SupNorm_V(ISigmaDiffDataBuffer<float> value)
		{
			if (value.Length == 0)
			{
				return 0.0f;
			}

			fixed (float* aref = &value.Data[value.Offset])
			{
				int len = value.Length;
				int inca = 1;

				int i = BlasBackend.Isamax(&len, aref, &inca);

				return value.Data[value.Offset + i - 1];
			}
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sum_V"/>
		public override float Sum_V(ISigmaDiffDataBuffer<float> a)
		{
			if (a.Length == 0)
			{
				return 0.0f;
			}

			int simdLength = Vector<float>.Count, len = a.Length;
			float[] aData = a.Data;
			int aOffset = a.Offset;
			float result = 0.0f;

			// Use SIMD instructions to sum all elements into a temporary vector vt, then sum that and the remaining items to result.
			fixed (float* aref = &aData[aOffset])
			{
				Vector<float> vt = Vector<float>.Zero;

				int i;
				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					vt += va;
				}

				for (; i < len; ++i)
				{
					result += aref[i];
				}

				result += Vector.Dot<float>(vt, Vector<float>.One);
			}

			return result;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sum_M"/>
		public override float Sum_M(ISigmaDiffDataBuffer<float> value)
		{
			return Sum_V(value);
		}

		public override int MaxIndex_V(ISigmaDiffDataBuffer<float> a)
		{
			if (a.Length == 0)
			{
				return 0;
			}

			int simdLength = Vector<float>.Count, len = a.Length;
			float[] aData = a.Data;
			int aOffset = a.Offset, maxIndex = 0;
			float maxValue = float.NegativeInfinity;

			// Pick between per-item (for smaller arrays) and SIMD max index search (for larger arrays).
			fixed (float* aref = &aData[aOffset])
			{
				// In case of smaller array, use standard manual max index search.
				if (len < simdLength * 4)
				{
					for (int k = 0; k < len; k++)
					{
						if (aref[k] > maxValue)
						{
							maxValue = aref[k];
							maxIndex = k;
						}
					}
				}
				// In case of larger array, use SIMD max index search.
				// In an optimal case, this should perform around 4-5x times faster for large enough arrays with evenly distributed values.
				else
				{
					Vector<float> vt = new Vector<float>(float.NegativeInfinity);

					// Find the maximum values for each simdLength vector slot.
					int i = 0;
					for (; i <= len - simdLength; i += simdLength)
					{
						Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
						vt = Vector.Max(va, vt);
					}

					// Find the maximum value within the remaining data.
					for (; i < len; ++i)
					{
						if (aref[i] > maxValue)
						{
							maxValue = aref[i];
							maxIndex = i;
						}
					}

					// If any of the maximum value slots of the SIMD vector is larger than the already picked value, set the mod max index (slot index within the SIMD vector).
					int modMaxIndex = -1;
					for (int y = 0; y < simdLength; y++)
					{
						if (vt[y] > maxValue)
						{
							maxValue = vt[y];
							modMaxIndex = y;
						}
					}

					// If the slot ("mod") index was set, find the actual max index in simdLength increments.
					if (modMaxIndex != -1)
					{
						for (i = modMaxIndex; i < len; i += simdLength)
						{
							if (aref[i] == maxValue)
							{
								maxIndex = i;

								break;
							}
						}
					}
				}
			}

			return maxIndex;
		}

		public override int MinIndex_V(ISigmaDiffDataBuffer<float> a)
		{
			if (a.Length == 0)
			{
				return 0;
			}

			int simdLength = Vector<float>.Count, len = a.Length;
			float[] aData = a.Data;
			int aOffset = a.Offset, minIndex = 0;
			float minValue = float.PositiveInfinity;

			// Pick between per-item (for smaller arrays) and SIMD min index search (for larger arrays).
			fixed (float* aref = &aData[aOffset])
			{
				// In case of smaller array, use standard manual max index search.
				if (len < simdLength * 4)
				{
					for (int k = 0; k < len; k++)
					{
						if (aref[k] < minValue)
						{
							minValue = aref[k];
							minIndex = k;
						}
					}
				}
				// In case of larger array, use SIMD min index search.
				// In an optimal case, this should perform around 4-5x times faster for large enough arrays with evenly distributed values.
				else
				{
					Vector<float> vt = new Vector<float>(float.PositiveInfinity);

					// Find the minimum values for each simdLength vector slot.
					int i = 0;
					for (; i <= len - simdLength; i += simdLength)
					{
						Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
						vt = Vector.Min(va, vt);
					}

					// Find the minimum value within the remaining data.
					for (; i < len; ++i)
					{
						if (aref[i] < minValue)
						{
							minValue = aref[i];
							minIndex = i;
						}
					}

					// If any of the minimum value slots of the SIMD vector is smaller than the already picked value, set the mod min index (slot index within the SIMD vector).
					int modMinIndex = -1;
					for (int y = 0; y < simdLength; y++)
					{
						if (vt[y] < minValue)
						{
							minValue = vt[y];
							modMinIndex = y;
						}
					}

					// If the slot ("mod") index was set, find the actual min index in simdLength increments.
					if (modMinIndex != -1)
					{
						for (i = modMinIndex; i < len; i += simdLength)
						{
							if (aref[i] == minValue)
							{
								minIndex = i;

								break;
							}
						}
					}
				}
			}

			return minIndex;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Add_V_V"/>
		public override ISigmaDiffDataBuffer<float> Add_V_V(ISigmaDiffDataBuffer<float> a, ISigmaDiffDataBuffer<float> b)
		{
			if (a.Length == 0)
			{
				return b.DeepCopy();
			}
			if (b.Length == 0)
			{
				return a.DeepCopy();
			}

			b = b.DeepCopy();
			fixed (float* aref = &a.Data[a.Offset])
			fixed (float* bref = &b.Data[b.Offset])
			{
				int len = Math.Min(a.Length, b.Length);
				int inca = 1, incb = 1;
				float alpha = 1.0f;

				BlasBackend.Saxpy(&len, &alpha, aref, &inca, bref, &incb);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Add_V_V_InPlace"/>
		public override ISigmaDiffDataBuffer<float> Add_V_V_InPlace(ISigmaDiffDataBuffer<float> a, int aOffset, ISigmaDiffDataBuffer<float> b, int bOffset, int len)
		{
			if (len == 0)
			{
				return b;
			}

			fixed (float* aref = &a.Data[a.Offset + aOffset])
			fixed (float* bref = &b.Data[b.Offset + bOffset])
			{
				int inca = 1, incb = 1;
				float alpha = 1.0f;

				BlasBackend.Saxpy(&len, &alpha, aref, &inca, bref, &incb);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Add_S_V"/>
		public override ISigmaDiffDataBuffer<float> Add_S_V(float a, ISigmaDiffDataBuffer<float> b)
		{
			if (b.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			b = b.DeepCopy();
			fixed (float* bref = &b.Data[b.Offset])
			{
				int len = Math.Min(1, b.Length);
				int inca = 0, incb = 1;
				float alpha = 1.0f;

				BlasBackend.Saxpy(&len, &alpha, &a, &inca, bref, &incb);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sub_V_V"/>
		public override ISigmaDiffDataBuffer<float> Sub_V_V(ISigmaDiffDataBuffer<float> a, ISigmaDiffDataBuffer<float> b)
		{
			if (a.Length == 0)
			{
				return b.DeepCopy();
			}
			if (b.Length == 0)
			{
				return a.DeepCopy();
			}

			b = b.DeepCopy();
			fixed (float* aref = &a.Data[a.Offset])
			fixed (float* bref = &b.Data[b.Offset])
			{
				int len = Math.Min(a.Length, b.Length);
				int inca = 1, incb = 1;
				float alpha = -1.0f;

				BlasBackend.Saxpy(&len, &alpha, bref, &incb, aref, &inca);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sub_S_V"/>
		public override ISigmaDiffDataBuffer<float> Sub_S_V(float a, ISigmaDiffDataBuffer<float> b)
		{
			if (b.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			b = b.DeepCopy();
			fixed (float* bref = &b.Data[b.Offset])
			{
				int len = b.Length;
				int inca = 0, incb = 1;
				float alpha = -1.0f;

				BlasBackend.Saxpy(&len, &alpha, &a, &inca, bref, &incb);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sub_V_S"/>
		public override ISigmaDiffDataBuffer<float> Sub_V_S(ISigmaDiffDataBuffer<float> a, float b)
		{
			if (a.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			a = a.DeepCopy();
			fixed (float* aref = &a.Data[a.Offset])
			{
				int len = a.Length;
				int inca = 1, incb = 0;
				float alpha = -1.0f;

				BlasBackend.Saxpy(&len, &alpha, aref, &inca, &b, &incb);
			}

			return a;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_S_V"/>
		public override ISigmaDiffDataBuffer<float> Mul_S_V(float a, ISigmaDiffDataBuffer<float> b)
		{
			if (b.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			b = b.DeepCopy();
			fixed (float* bref = &b.Data[b.Offset])
			{
				int len = b.Length;
				int incx = 1;

				BlasBackend.Sscal(&len, &a, bref, &incx);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_M_V"/>
		public override ISigmaDiffDataBuffer<float> Mul_M_V(ShapedDataBufferView<float> a, ISigmaDiffDataBuffer<float> b)
		{
			if (a.Length * b.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			ISigmaDiffDataBuffer<float> z = CreateDataBuffer(CreateZeroArray(a.Rows));

			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			fixed (float* bref = &b.Data[b.Offset])
			fixed (float* zref = &z.Data[z.Offset])
			{
				char trans = 'T';
				int m = a.Cols, n = a.Rows;
				int incb = 1, incz = 1;
				float alpha = 1.0f, beta = 0.0f;

				BlasBackend.Sgemv(&trans, &m, &n, &alpha, aref, &m, bref, &incb, &beta, zref, &incz);
			}

			return z;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_M_V_Add_V"/>
		public override ISigmaDiffDataBuffer<float> Mul_M_V_Add_V(ShapedDataBufferView<float> a, ISigmaDiffDataBuffer<float> b, ISigmaDiffDataBuffer<float> obj2)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_Dot_V_V"/>
		public override float Mul_Dot_V_V(ISigmaDiffDataBuffer<float> a, ISigmaDiffDataBuffer<float> n)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_V_M"/>
		public override ISigmaDiffDataBuffer<float> Mul_V_M(ISigmaDiffDataBuffer<float> a, ShapedDataBufferView<float> b)
		{
			if (a.Length * b.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			ISigmaDiffDataBuffer<float> z = CreateDataBuffer(CreateZeroArray(b.Rows));

			fixed (float* aref = &a.Data[a.Offset])
			fixed (float* bref = &b.DataBuffer.Data[b.DataBuffer.Offset])
			fixed (float* zref = &z.Data[z.Offset])
			{
				char trans = 'T';
				int m = b.Cols, n = b.Rows;
				int incb = 1, incz = 1;
				float alpha = 1.0f, beta = 0.0f;

				BlasBackend.Sgemv(&trans, &m, &n, &alpha, aref, &m, bref, &incb, &beta, zref, &incz);
			}

			return z;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Solve_M_V"/>
		public override FSharpOption<ISigmaDiffDataBuffer<float>> Solve_M_V(ShapedDataBufferView<float> a, ISigmaDiffDataBuffer<float> b)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.SolveSymmetric_M_V"/>
		public override FSharpOption<ISigmaDiffDataBuffer<float>> SolveSymmetric_M_V(ShapedDataBufferView<float> a, ISigmaDiffDataBuffer<float> b)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Diagonal_M"/>
		public override ISigmaDiffDataBuffer<float> Diagonal_M(ShapedDataBufferView<float> a)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Map_F_V"/>
		public override ISigmaDiffDataBuffer<float> Map_F_V(MapOp mapOp, FSharpFunc<float, float> a, ISigmaDiffDataBuffer<float> b)
		{
			if (b.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			b = b.DeepCopy();

			int upper = b.Offset + b.Length;
			for (int i = b.Offset; i < upper; i++)
			{
				b.Data[i] = a.Invoke(b.Data[i]);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Map_F_S_V"/>
		public override ISigmaDiffDataBuffer<float> Map_F_S_V(float other, MapOp mapOp, FSharpFunc<float, float> function, ISigmaDiffDataBuffer<float> value)
		{
			return Map_F_V(mapOp, function, value);
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Map2_F_V_V"/>
		public override ISigmaDiffDataBuffer<float> Map2_F_V_V(MapOp mapOp, FSharpFunc<float, FSharpFunc<float, float>> function, ISigmaDiffDataBuffer<float> a, ISigmaDiffDataBuffer<float> b)
		{
			if (a.Length == 0)
			{
				return Map2_F_V_V(mapOp, function, CreateDataBuffer(CreateZeroArray(b.Length)), b);
			}
			if (b.Length == 0)
			{
				return Map2_F_V_V(mapOp, function, a, CreateDataBuffer(CreateZeroArray(a.Length)));
			}

			b = b.DeepCopy();

			for (int i = 0; i < a.Length; i++)
			{
				b.Data[i] = function.Invoke(a.Data[i + a.Offset]).Invoke(b.Data[i + b.Offset]);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_Out_V_V"/>
		public override ShapedDataBufferView<float> Mul_Out_V_V(ISigmaDiffDataBuffer<float> a, ISigmaDiffDataBuffer<float> b)
		{
			if (a.Length * b.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			ISigmaDiffDataBuffer<float> z = CreateDataBuffer(CreateZeroArray(a.Length * b.Length));
			int m = b.Length, n = a.Length;

			fixed (float* aref = &a.Data[a.Offset])
			fixed (float* bref = &b.Data[b.Offset])
			fixed (float* zref = &z.Data[z.Offset])
			{
				int inca = 1, incb = 1;
				float alpha = 1.0f;

				BlasBackend.Sger(&m, &n, &alpha, aref, &inca, bref, &incb, zref, &m);
			}

			return new ShapedDataBufferView<float>(z, m, n);
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Add_M_M"/>
		public override ShapedDataBufferView<float> Add_M_M(ShapedDataBufferView<float> a, ShapedDataBufferView<float> b)
		{
			if (a.Length == 0)
			{
				return b.DeepCopy();
			}
			if (b.Length == 0)
			{
				return a.DeepCopy();
			}

			b = b.DeepCopy();
			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			fixed (float* bref = &b.DataBuffer.Data[b.DataBuffer.Offset])
			{
				int len = Math.Min(a.Length, b.Length);
				int inca = 1, incb = 1;
				float alpha = 1.0f;

				BlasBackend.Saxpy(&len, &alpha, aref, &inca, bref, &incb);
			}

			return b;
		}

		public override ShapedDataBufferView<float> Add_M_M_InPlace(ShapedDataBufferView<float> a, ShapedDataBufferView<float> b)
		{
			if (a.Length == 0)
			{
				return b;
			}
			if (b.Length == 0)
			{
				return a;
			}

			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			fixed (float* bref = &b.DataBuffer.Data[b.DataBuffer.Offset])
			{
				int len = Math.Min(a.Length, b.Length);
				int inca = 1, incb = 1;
				float alpha = 1.0f;

				BlasBackend.Saxpy(&len, &alpha, aref, &inca, bref, &incb);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Add_S_M"/>
		public override ShapedDataBufferView<float> Add_S_M(float a, ShapedDataBufferView<float> b)
		{
			if (b.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			b = b.DeepCopy();
			fixed (float* bref = &b.DataBuffer.Data[b.DataBuffer.Offset])
			{
				int len = b.Length;
				int inca = 0, incb = 1;
				float alpha = 1.0f;

				BlasBackend.Saxpy(&len, &alpha, &a, &inca, bref, &incb);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Add_V_MCols"/>
		public override ShapedDataBufferView<float> Add_V_MCols(ISigmaDiffDataBuffer<float> a, ShapedDataBufferView<float> b)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sub_M_M"/>
		public override ShapedDataBufferView<float> Sub_M_M(ShapedDataBufferView<float> a, ShapedDataBufferView<float> b)
		{
			if (a.Length == 0)
			{
				return b.DeepCopy();
			}
			if (b.Length == 0)
			{
				return a.DeepCopy();
			}

			a = a.DeepCopy();
			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			fixed (float* bref = &b.DataBuffer.Data[b.DataBuffer.Offset])
			{
				int len = Math.Min(a.Length, b.Length);
				int inca = 1, incb = 1;
				float alpha = -1.0f;

				BlasBackend.Saxpy(&len, &alpha, bref, &incb, aref, &inca);
			}

			return a;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sub_M_S"/>
		public override ShapedDataBufferView<float> Sub_M_S(ShapedDataBufferView<float> a, float b)
		{
			if (a.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			a = a.DeepCopy();
			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			{
				int len = a.Length;
				int inca = 1, incb = 0;
				float alpha = -1.0f;

				BlasBackend.Saxpy(&len, &alpha, &b, &incb, aref, &inca);
			}

			return a;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Sub_S_M"/>
		public override ShapedDataBufferView<float> Sub_S_M(float other, ShapedDataBufferView<float> a)
		{
			if (a.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			a = a.DeepCopy();

			int simdLength = Vector<float>.Count;
			int len = a.Length;
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(len)), (long[])a.Shape.Clone());
			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;
			Vector<float> vc = new Vector<float>(other); // filled with constant value of other

			// Use SIMD instructions to subtract an array from a constant factor (using a vc array filled with the constant a).
			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int i;
				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					(vc - va).CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = other - aref[i];
				}
			}

			return result;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_M_M"/>
		public override ShapedDataBufferView<float> Mul_M_M(ShapedDataBufferView<float> a, ShapedDataBufferView<float> b)
		{
			if (a.Length * b.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			ISigmaDiffDataBuffer<float> z = CreateDataBuffer(new float[a.Rows * b.Cols]);

			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			fixed (float* bref = &b.DataBuffer.Data[b.DataBuffer.Offset])
			fixed (float* zref = &z.Data[z.Offset])
			{
				char transa = 'N', transb = 'N';
				float alpha = 1.0f, beta = 0.0f;
				int m = a.Rows, n = b.Cols, k = b.Rows;

				BlasBackend.Sgemm(&transa, &transb, &n, &m, &k, &alpha, bref, &n, aref, &k, &beta, zref, &n);
			}

			return new ShapedDataBufferView<float>(z, a.Rows, b.Cols);
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_S_M"/>
		public override ShapedDataBufferView<float> Mul_S_M(float a, ShapedDataBufferView<float> b)
		{
			if (b.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			b = b.DeepCopy();
			fixed (float* bref = &b.DataBuffer.Data[b.DataBuffer.Offset])
			{
				int len = b.Length;
				int incx = 1;

				BlasBackend.Sscal(&len, &a, bref, &incx);
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_M_M_Add_V_MCols"/>
		public override ShapedDataBufferView<float> Mul_M_M_Add_V_MCols(ShapedDataBufferView<float> a, ShapedDataBufferView<float> b, ISigmaDiffDataBuffer<float> c)
		{
			throw new NotImplementedException();
		}

		public override ISigmaDiffDataBuffer<float> Add_M_Colwise_V_InPlace(ShapedDataBufferView<float> a, ISigmaDiffDataBuffer<float> b)
		{
			float[] aData = a.DataBuffer.Data, bData = b.Data;
			int aOffset = a.DataBuffer.Offset, bOffset = b.Offset;
			int cols = a.Cols, len = b.Length;

			// Use SIMD instructions to multiply two arrays element-wise.
			fixed (float* aref = &aData[aOffset])
			fixed (float* bref = &bData[bOffset])
			{
				int simdLength = Vector<float>.Count, i;

				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> vb = new Vector<float>(bData, i + bOffset);

					for (int y = 0; y < a.Rows; y++)
					{
						vb += new Vector<float>(aData, i + aOffset + y * cols);
					}

					vb.CopyTo(bData, i + bOffset);
				}

				for (; i < len; ++i)
				{
					for (int y = 0; y < a.Rows; y++)
					{
						bref[i] += aref[i + y * cols];
					}
				}
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Mul_Had_M_M"/>
		public override unsafe ShapedDataBufferView<float> Mul_Had_M_M(ShapedDataBufferView<float> a, ShapedDataBufferView<float> b)
		{
			if (a.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(CreateZeroArray(b.Length)), b.Shape);
			}
			if (b.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(CreateZeroArray(a.Length)), a.Shape);
			}

			int len = Math.Min(a.Length, b.Length);
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(len)), (long[])b.Shape.Clone());

			float[] aData = a.DataBuffer.Data, bData = b.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, bOffset = b.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;

			// Use SIMD instructions to multiply two arrays element-wise.
			fixed (float* aref = &aData[aOffset])
			fixed (float* bref = &bData[bOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int simdLength = Vector<float>.Count, i;

				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					Vector<float> vb = new Vector<float>(bData, i + bOffset);
					(va * vb).CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = aref[i] * bref[i];
				}
			}

			return result;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Inverse_M"/>
		public override FSharpOption<ShapedDataBufferView<float>> Inverse_M(ShapedDataBufferView<float> a)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Det_M"/>
		public override FSharpOption<float> Det_M(ShapedDataBufferView<float> a)
		{
			if (a.Length == 0)
			{
				return FSharpOption<float>.Some(0.0f);
			}

			a = a.DeepCopy();

			int info = 0;
			int[] ipiv = new int[Math.Min(a.Rows, a.Cols)];

			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			fixed (int* ipivref = &ipiv[0])
			{
				int m = a.Rows, n = a.Cols;

				LapackBackend.Sgetrf_(&m, &n, aref, &m, ipivref, &info);
			}

			if (info != 0)
			{
				return FSharpOption<float>.None;
			}

			float det = 1.0f;

			for (int i = 0; i < ipiv.Length; i++)
			{
				det *= ipiv[i] != i + 1 ? -a[i, i] : a[i, i];
			}

			return FSharpOption<float>.Some(det);
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Transpose_M"/>
		public override ShapedDataBufferView<float> Transpose_M(ShapedDataBufferView<float> a)
		{
			if (a.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			ShapedDataBufferView<float> transposed = a.DeepCopy();

			for (int i = 0; i < transposed.Shape.Length; i++)
			{
				transposed.Shape[i] = a.Shape[a.Shape.Length - 1 - i];
			}

			fixed (float* aref = &a.DataBuffer.Data[a.DataBuffer.Offset])
			fixed (float* bref = &transposed.DataBuffer.Data[transposed.DataBuffer.Offset])
			{
				int ordering = 101; // CBLAS_LAYOUT - CblasRowMajor
				int trans = 112; // CBLAS_TRANSPOSE - CblasTrans
				int rows = a.Rows, cols = a.Cols;
				int lda = a.Cols, ldb = a.Rows;
				float alpha = 1.0f;

				BlasBackend.Somatcopy(ordering, trans, rows, cols, alpha, aref, lda, bref, ldb);
			}

			return transposed;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Permute_M"/>
		public override ShapedDataBufferView<float> Permute_M(ShapedDataBufferView<float> a, int[] permutedDimensions)
		{
			long[] originalShape = a.Shape;
			long[] permutedShape = ArrayUtils.PermuteArray(originalShape, permutedDimensions);
			long[] originalStrides = NDArrayUtils.GetStrides(originalShape), permutedStrides = NDArrayUtils.GetStrides(permutedShape);
			int rank = originalShape.Length, unitSize = 1;

			for (int i = permutedDimensions.Length - 1; i >= 0; i--)
			{
				if (permutedDimensions[i] != i)
				{
					break;
				}

				unitSize = checked(unitSize * (int)originalShape[i]);
			}

			int unitSizeBytes = unitSize * sizeof(float), len = a.Length;
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(len)), permutedShape);
			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;

			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				long[] bufferIndices = new long[rank];

				if (unitSize > 1) // TODO maybe set a limit for when block copy outperforms manual copy (don't forget to change i++ in else branch)
				{
					for (int i = 0; i < len; i += unitSize)
					{
						NDArrayUtils.GetIndices(i, originalShape, originalStrides, bufferIndices);

						bufferIndices = ArrayUtils.PermuteArray(bufferIndices, permutedDimensions);

						int resultIndex = (int)NDArrayUtils.GetFlatIndex(permutedShape, permutedStrides, bufferIndices);

						Buffer.BlockCopy(aData, (i + aOffset) * sizeof(float), resData, (resultIndex + resOffset) * sizeof(float), unitSizeBytes);
					}
				}
				else
				{
					for (int i = 0; i < len; i++)
					{
						NDArrayUtils.GetIndices(i, originalShape, originalStrides, bufferIndices);

						bufferIndices = ArrayUtils.PermuteArray(bufferIndices, permutedDimensions);

						int resultIndex = (int)NDArrayUtils.GetFlatIndex(permutedShape, permutedStrides, bufferIndices);

						resref[resultIndex] = aref[i];
					}
				}
			}

			return result;
		}

		public override ShapedDataBufferView<float> Reshape_M(ShapedDataBufferView<float> array, long[] newShape)
		{
			ShapedDataBufferView<float> reshaped = new ShapedDataBufferView<float>(array.DataBuffer, newShape);

			return reshaped;
		}

		private bool _InternalOptimisedMapOp_F_M(MapOp mapOp, ref ShapedDataBufferView<float> a)
		{
			if (mapOp.IsExp)
			{
				_InternalOptimisedExp(ref a);

				return true;
			}
			else if (mapOp.IsSqrt)
			{
				_InternalOptimisedSqrt(ref a);

				return true;
			}
			else if (mapOp.IsSign)
			{
				_InternalOptimisedSign(ref a);

				return true;
			}
			else if (mapOp.IsReL)
			{
				_InternalOptimisedRel(ref a);

				return true;
			}
			else if (mapOp.IsSigmoid)
			{
				_InternalOptimisedSigmoid(ref a);

				return true;
			}
			else if (mapOp.IsLog)
			{
				_InternalOptimisedLog(ref a);

				return true;
			}

			return false;
		}

		private unsafe void _InternalOptimisedSign(ref ShapedDataBufferView<float> a)
		{
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(a.Length)), (long[])a.Shape.Clone());

			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;
			int len = a.Length;

			uint signFlag = 0x80000000;
			Vector<float> vf = new Vector<float>(*(float*)&signFlag);

			// Use SIMD instructions with a few bit-twiddling "hacks" to quickly get the sign for each element in a large array.
			// This works because the sign if the first bit in the binary floating point representation which can be easily extracted using bitwise operations.
			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int simdLength = Vector<float>.Count, i;

				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					Vector<float> vr = Vector<float>.One;
					(vr | (va & vf)).CopyTo(resData, i + resOffset); // TODO should zero be considered as well? (right now it's 1 / -1 only)
				}

				for (; i < len; ++i)
				{
					resref[i] = 1.0f;
					resref[i] = *(uint*)&resref[i] | (*(uint*)&aref[i] & signFlag);
				}
			}

			a = result;
		}

		private void _InternalOptimisedSqrt(ref ShapedDataBufferView<float> a)
		{
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(a.Length)), (long[])a.Shape.Clone());

			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;
			int len = a.Length;

			// Use SIMD instructions to quickly get the square root of each element of an array.
			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int simdLength = Vector<float>.Count, i;

				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					Vector.SquareRoot(va).CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = (float)Math.Sqrt(aref[i]);
				}
			}

			a = result; // write out result to ref
		}

		private static void _InternalOptimisedExp(ref ShapedDataBufferView<float> a)
		{
			a = a.DeepCopy();
			float[] aData = a.DataBuffer.Data;
			Vector<float> vf = new Vector<float>(1.0f / 256.0f);
			Vector<float> vc = Vector<float>.One;
			int len = a.Length, simdLength = Vector<float>.Count, aOffset = a.DataBuffer.Offset;

			// Use SIMD instructions and an exponent estimation to quickly get the exponent of each element of an array.
			// This works because the exponential function e^x can be considered as the limit of (1 + x/n)^n for n -> infinity.
			// For any reasonably small value for machine learning (< ~7) the error is extremely small when approximated with
			//  x = 1 + x / 256
			//	x *= x; x *= x; x *= x; x *= x; x *= x; x *= x; x *= x; x *= x; // 8 times, because 2^8 is 256 and that's reasonably accurate.
			fixed (float* aref = &aData[aOffset])
			{
				int i;
				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> vres = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					vres = vc + vres * vf;

					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;

					vres.CopyTo(aData, i + aOffset);
				}

				for (; i < len; ++i)
				{
					aref[i] = (float)Math.Exp(aref[i]);
				}
			}
		}

		private void _InternalOptimisedRel(ref ShapedDataBufferView<float> a)
		{
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(a.Length)), (long[])a.Shape.Clone());

			int simdLength = Vector<float>.Count;
			int len = a.Length;
			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;
			Vector<float> vc = Vector<float>.Zero;

			// Use SIMD instructions and an exponent estimation to quickly get the exponent of each element of an array.
			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int i;
				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					Vector.Max(va, vc).CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = Math.Max(aref[i], 0.0f);
				}
			}

			a = result;
		}

		// number1 / (number1 + exp -v))
		private void _InternalOptimisedSigmoid(ref ShapedDataBufferView<float> a)
		{
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(a.Length)), (long[])a.Shape.Clone());

			int simdLength = Vector<float>.Count;
			int len = a.Length;
			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;
			Vector<float> vc = Vector<float>.One;
			Vector<float> vf = new Vector<float>(1.0f / 256.0f);

			// Use SIMD instructions and an exponent estimation to quickly get the sigmoid of each element of an array.
			// The exponent estimation works because the exponential function e^x can be considered as the limit of (1 + x/n)^n for n -> infinity.
			// For any reasonably small value for machine learning (< ~7) the error is extremely small when approximated with
			//  x = 1 + x / 256
			//	x *= x; x *= x; x *= x; x *= x; x *= x; x *= x; x *= x; x *= x; // 8 times, because 2^8 is 256 and that's reasonably accurate.
			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int i;
				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> vres = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					vres = vc - vres * vf;

					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;
					vres *= vres;

					vres = vres + vc;
					(vc / vres).CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = 1.0f / (1.0f + (float)Math.Exp(-aref[i]));
				}
			}

			a = result;
		}

		private void _InternalOptimisedLog(ref ShapedDataBufferView<float> a)
		{
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(a.Length)), (long[])a.Shape.Clone());

			int simdLength = Vector<float>.Count;
			int len = a.Length;
			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;

			// Using SIMD instructions, bit-twiddling and rational approximation to quickly get the log of each element of an array.
			// Quite frankly, I'm not quite sure yet just why this works, it just does, and with quite high accuracy (avg error of 1.60712e-05 for [0, 20])
			// See https://github.com/etheory/fastapprox/blob/master/fastapprox/src/fastlog.h
			uint exponentMask = 0x3f000000, mantissaMask = 0x007fffff;
			Vector<float> vf1 = new Vector<float>(*(float*) &exponentMask);
			Vector<float> vf2 = new Vector<float>(*(float*) &mantissaMask);
			Vector<float> vc1 = new Vector<float>(1.1920928955078125e-7f);
			Vector<float> vc2 = new Vector<float>(124.22551499f);
			Vector<float> vc3 = new Vector<float>(1.498030302f);
			Vector<float> vc4 = new Vector<float>(1.72587999f);
			Vector<float> vc5 = new Vector<float>(0.3520887068f);
			Vector<float> vc6 = new Vector<float>(0.69314718f);
			float[] vaiBuffer = CreateUninitialisedArray(simdLength);

			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int i;
				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets

					// TODO the manual float bits as int then back cast to float conversion is taking a lot of time (~70% of the entire method)
					for (int y = 0; y < simdLength; y++)
					{
						vaiBuffer[y] = *(uint*) &aref[i + y];
					}

					Vector<float> vai = new Vector<float>(vaiBuffer);
					Vector<float> vt1 = (va & vf2) | vf1;
					Vector<float> vres = vc1 * vai;
					Vector<float> vt2 = (vc3 * vt1);
					Vector<float> vt3 = (vc4 / (vc5 + vt1));
					vres = (vres - vc2 - vt2 - vt3) * vc6; 

					vres.CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = (float)Math.Log(aref[i]);
				}
			}

			a = result;
		}

		private bool _InternalOptimisedMapOp_F_S_M(float other, MapOp mapOp, ref ShapedDataBufferView<float> a)
		{
			if (mapOp.IsDiv)
			{
				_InternalOptimisedDiv(other, ref a);

				return true;
			}

			return false;
		}

		private void _InternalOptimisedDiv(float other, ref ShapedDataBufferView<float> a)
		{
			int simdLength = Vector<float>.Count;
			int len = a.Length;
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(len)), (long[])a.Shape.Clone());
			float[] aData = a.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;
			Vector<float> vc = new Vector<float>(other); // filled with constant value of other

			// Use SIMD instructions to divide an array by a constant factor (using a vc array filled with the constant a).
			fixed (float* aref = &aData[aOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int i;
				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					(vc / va).CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = other / aref[i];
				}
			}

			a = result;
		}

		private bool _InternalOptimisedMapOp_F_M_M(MapOp mapOp, ShapedDataBufferView<float> a, ref ShapedDataBufferView<float> b)
		{
			if (mapOp.IsDiv)
			{
				_InternalOptimisedDiv(a, ref b);

				return true;
			}

			return false;
		}

		private void _InternalOptimisedDiv(ShapedDataBufferView<float> a, ref ShapedDataBufferView<float> b)
		{
			int len = Math.Min(a.Length, b.Length);
			ShapedDataBufferView<float> result = new ShapedDataBufferView<float>(CreateDataBuffer(CreateUninitialisedArray(len)), (long[])b.Shape.Clone());

			float[] aData = a.DataBuffer.Data, bData = b.DataBuffer.Data, resData = result.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, bOffset = b.DataBuffer.Offset, resOffset = result.DataBuffer.Offset;

			// Use SIMD instructions to divide an array by another array.
			fixed (float* aref = &aData[aOffset])
			fixed (float* bref = &bData[bOffset])
			fixed (float* resref = &resData[resOffset])
			{
				int simdLength = Vector<float>.Count, i;

				for (i = 0; i <= len - simdLength; i += simdLength)
				{
					Vector<float> va = new Vector<float>(aData, i + aOffset); // TODO optimise offsets
					Vector<float> vb = new Vector<float>(bData, i + bOffset);
					(va / vb).CopyTo(resData, i + resOffset);
				}

				for (; i < len; ++i)
				{
					resref[i] = aref[i] / bref[i];
				}
			}

			b = result;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Map_F_M"/>
		public override ShapedDataBufferView<float> Map_F_M(MapOp mapOp, FSharpFunc<float, float> f, ShapedDataBufferView<float> a)
		{
			if (a.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			if (_InternalOptimisedMapOp_F_M(mapOp, ref a))
			{
				return a;
			}

			a = a.DeepCopy();

			int upper = a.DataBuffer.Offset + a.DataBuffer.Length;
			float[] data = a.DataBuffer.Data;

			for (int i = a.DataBuffer.Offset; i < upper; i++)
			{
				data[i] = f.Invoke(data[i]);
			}

			return a;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Map_F_S_M"/>
		public override ShapedDataBufferView<float> Map_F_S_M(float other, MapOp mapOp, FSharpFunc<float, float> function, ShapedDataBufferView<float> value)
		{
			if (_InternalOptimisedMapOp_F_S_M(other, mapOp, ref value))
			{
				return value;
			}

			return Map_F_M(mapOp, function, value); // this is correct, the "other" constant is only passed to speed up computation in optimised versions without using the lambda
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.Map2_F_M_M"/>
		public override ShapedDataBufferView<float> Map2_F_M_M(MapOp mapOp, FSharpFunc<float, FSharpFunc<float, float>> f, ShapedDataBufferView<float> a, ShapedDataBufferView<float> b)
		{
			if (a.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}
			if (b.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			if (_InternalOptimisedMapOp_F_M_M(mapOp, a, ref b))
			{
				return b;
			}

			// If no optimised versions exist for the given map operation, manually invoke the given function for each element (slow).
			b = b.DeepCopy();

			float[] aData = a.DataBuffer.Data, bData = b.DataBuffer.Data;
			int aOffset = a.DataBuffer.Offset, bOffset = b.DataBuffer.Offset;

			fixed (float* aref = &aData[aOffset])
			fixed (float* bref = &bData[bOffset])
			{
				for (int i = 0; i < a.Length; i++)
				{
					bref[i] = f.Invoke(aref[i]).Invoke(bData[i]);
				}
			}

			return b;
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.ReshapeCopy_MRows_V"/>
		public override ISigmaDiffDataBuffer<float> ReshapeCopy_MRows_V(ShapedDataBufferView<float> value)
		{
			if (value.Length == 0)
			{
				return CreateDataBuffer(new float[0]);
			}

			return value.DataBuffer.DeepCopy();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.ReshapeCopy_V_MRows"/>
		public override ShapedDataBufferView<float> ReshapeCopy_V_MRows(int rows, ISigmaDiffDataBuffer<float> value)
		{
			if (value.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			int n = value.Length / rows;

			return new ShapedDataBufferView<float>(value.DeepCopy(), rows, n);
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.RepeatReshapeCopy_V_MRows"/>
		public override ShapedDataBufferView<float> RepeatReshapeCopy_V_MRows(int rows, ISigmaDiffDataBuffer<float> row)
		{
			if (row.Length == 0)
			{
				return new ShapedDataBufferView<float>(CreateDataBuffer(new float[0]), 0L, 0L);
			}

			int rowLength = row.Length;
			float[] result = CreateUninitialisedArray(rows * rowLength);
			float[] rowData = row.Data;
			int sourceOffset = row.Offset;
			int destinationOffset = 0;

			for (int i = 0; i < rows; i++)
			{
				Buffer.BlockCopy(rowData, sourceOffset * sizeof(float), result, destinationOffset * sizeof(float), rowLength * sizeof(float));

				destinationOffset += rowLength;
			}

			return new ShapedDataBufferView<float>(CreateDataBuffer(result), rows, rowLength);
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.RepeatReshapeCopy_V_MCols"/>
		public override ShapedDataBufferView<float> RepeatReshapeCopy_V_MCols(int cols, ISigmaDiffDataBuffer<float> value)
		{
			throw new NotImplementedException();
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.CustomOp_DM_Forward"/>
		public override ShapedDataBufferView<float> CustomOp_DM_Forward(ShapedDataBufferView<float> value, object customInfo)
		{
			throw new NotImplementedException($"Custom DM ops are not supported in default {nameof(DiffSharpFloat32BackendHandle)} implementation.");
		}

		/// <inheritdoc cref="DiffSharpBackendHandle{T}.CustomOp_DM_Backward"/>
		public override ShapedDataBufferView<float> CustomOp_DM_Backward(ShapedDataBufferView<float> origin, 
			ShapedDataBufferView<float> adjoint, ShapedDataBufferView<float> primal, object customInfo)
		{
			throw new NotImplementedException($"Custom DM ops are not supported in default {nameof(DiffSharpFloat32BackendHandle)} implementation.");
		}
	}
}
