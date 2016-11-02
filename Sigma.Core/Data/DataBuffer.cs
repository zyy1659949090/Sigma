﻿/* 
MIT License

Copyright (c) 2016 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using log4net;
using Sigma.Core.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sigma.Core.Data
{
	public class DataBuffer<T> : IDataBuffer<T>
	{
		private ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		private LargeChunkedArray<T> data;
		private long length;
		private long offset;
		private long relativeOffset;
		private IDataBuffer<T> underlyingBuffer;
		private IDataBuffer<T> underlyingRootBuffer;

		public long Length
		{
			get { return length; }
		}

		public long Offset
		{
			get { return offset; }
		}

		public long RelativeOffset
		{
			get { return relativeOffset; }
		}

		public IDataType Type
		{
			get; private set;
		}

		public ILargeChunkedArray<T> Data
		{
			get { return data; }
		}

		public DataBuffer(DataBuffer<T> underlyingBuffer, long offset, long length)
		{
			if (underlyingBuffer == null)
			{
				throw new NullReferenceException("Underlying buffer cannot be null.");
			}

			if (offset < 0)
			{
				throw new ArgumentException($"Offset must be > 0 but was {offset}.");
			}

			if (length < 1)
			{
				throw new ArgumentException($"Length must be > 1 but was {length}.");
			}

			if (offset + underlyingBuffer.offset + length > underlyingBuffer.length)
			{
				throw new ArgumentException("Buffer length cannot exceed length of its underlying buffer.");
			}

			this.length = length;
			this.relativeOffset = offset;
			this.offset = offset + underlyingBuffer.offset;

			this.data = underlyingBuffer.data;
			this.Type = underlyingBuffer.Type;
			this.underlyingBuffer = underlyingBuffer;
			this.underlyingRootBuffer = underlyingBuffer.underlyingRootBuffer == null ? underlyingBuffer : underlyingBuffer.underlyingRootBuffer;
		}

		public DataBuffer(LargeChunkedArray<T> data, long offset, long length, IDataType underlyingType = null)
		{
			if (data == null)
			{
				throw new NullReferenceException("Data cannot be null.");
			}

			if (offset < 0)
			{
				throw new ArgumentException($"Offset must be > 0 but was {offset}.");
			}

			if (length < 1)
			{
				throw new ArgumentException($"Length must be > 1 but was {length}.");
			}

			if (offset + length > data.Length)
			{
				throw new ArgumentException("Buffer length cannot exceed length of its underlying data array.");
			}

			this.data = data;
			this.length = length;
			this.relativeOffset = offset;
			this.offset = offset;

			this.Type = InferDataType(underlyingType);
		}

		public DataBuffer(long length, IDataType underlyingType = null)
		{
			if (length < 1)
			{
				throw new ArgumentException($"Length must be > 1 but was {length}.");
			}

			this.length = length;
			this.data = new LargeChunkedArray<T>(length);

			this.Type = InferDataType(underlyingType);
		}

		/// <summary>
		/// Copy constructor.
		/// </summary>
		/// <param name="other">The buffer to copy.</param>
		public DataBuffer(DataBuffer<T> other)
		{
			this.underlyingBuffer = other.underlyingBuffer;
			this.underlyingRootBuffer = other.underlyingRootBuffer;
			this.Type = other.Type;
			this.data = other.data;
			this.offset = other.offset;
			this.relativeOffset = other.relativeOffset;
			this.length = other.length;
		}
		
		private IDataType InferDataType(IDataType givenType)
		{
			if (givenType != null)
			{
				return givenType;
			}

			try
			{
				return DataTypes.GetMatchingType(typeof(T));
			}
			catch (ArgumentException e)
			{
				throw new ArgumentException($"Could not infer type interface for underlying system type {typeof(T)} (system type not registered) and no data type interface was explicitly given.", e);
			}
		}

		public IDataBuffer<T> Copy()
		{
			return new DataBuffer<T>(this);
		}

		public T GetValue(long index)
		{
			return data[offset + index];
		}

		public TOther GetValueAs<TOther>(long index)
		{
			return (TOther) Convert.ChangeType(index, typeof(TOther));
		}

		public IDataBuffer<T> GetValues(long startIndex, long length)
		{
			return new DataBuffer<T>(this, startIndex, length);
		}

		public IDataBuffer<TOther> GetValuesAs<TOther>(long startIndex, long length)
		{
			System.Type otherType = typeof(TOther);

			LargeChunkedArray<TOther> otherData = new LargeChunkedArray<TOther>(length);

			otherData.FillWith<T>(this.data, this.offset + startIndex, 0L, length);

			return new DataBuffer<TOther>(otherData, 0L, length);
		}

		public ILargeChunkedArray<T> GetValuesArray(long startIndex, long length)
		{
			LargeChunkedArray<T> valuesArray = new LargeChunkedArray<T>(length);

			valuesArray.FillWith(this.data, this.offset + startIndex, 0L, length);

			return valuesArray;
		}

		public ILargeChunkedArray<TOther> GetValuesArrayAs<TOther>(long startIndex, long length)
		{
			LargeChunkedArray<TOther> valuesArray = new LargeChunkedArray<TOther>(length);

			valuesArray.FillWith<T>(this.data, this.offset + startIndex, 0L, length);

			return valuesArray;
		}

		public void SetValue(T value, long index)
		{
			data[index + this.offset] = value;
		}

		public void SetValues(IDataBuffer<T> buffer, long sourceStartIndex, long destStartIndex, long length)
		{
			this.data.FillWith(buffer.Data, sourceStartIndex + buffer.Offset, destStartIndex + this.offset, length);
		}

		public void SetValues(T[] values, long sourceStartIndex, long destStartIndex, long length)
		{
			this.data.FillWith(values, sourceStartIndex, destStartIndex + this.offset, length);
		}

		public IDataBuffer<T> GetUnderlyingBuffer()
		{
			return underlyingBuffer;
		}

		public IDataBuffer<T> GetUnderlyingRootBuffer()
		{
			return underlyingRootBuffer;
		}

		public IEnumerator<T> GetEnumerator()
		{
			for (long i = 0; i < this.length; i++)
			{
				yield return data[i];
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			for (long i = 0; i < this.length; i++)
			{
				yield return data[i];
			}
		}
	}
}