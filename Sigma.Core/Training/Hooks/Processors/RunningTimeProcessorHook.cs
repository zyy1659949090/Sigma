﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using Sigma.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sigma.Core.Training.Hooks.Processors
{
	/// <summary>
	/// A running time processor hook to calculate last and average time between time scale events.
	/// </summary>
	[Serializable]
	public class RunningTimeProcessorHook : BaseHook
	{
		/// <summary>
		/// Create a hook with a certain time step and a set of required global registry entries. 
		/// </summary>
		/// <param name="averageSpan">The interval span to average over.</param>
		/// <param name="timeScale">The time scale this processor should run on (only individual time scales are supported).</param>
		public RunningTimeProcessorHook(TimeScale timeScale, int averageSpan = 4) : this(timeScale, averageSpan, $"shared.time_{timeScale}")
		{
		}

		/// <summary>
		/// Create a hook with a certain time step and a set of required global registry entries. 
		/// </summary>
		/// <param name="timeScale">The time scale.</param>
		/// <param name="averageSpan">The interval span to average over.</param>
		/// <param name="sharedResultBaseKey">The shared result base key (under which results will be available).</param>
		/// <param name="removeExtremas"></param>
		public RunningTimeProcessorHook(TimeScale timeScale, int averageSpan, string sharedResultBaseKey, bool removeExtremas = true) : base(Utils.TimeStep.Every(1, timeScale))
		{
			if (sharedResultBaseKey == null) throw new ArgumentNullException(nameof(sharedResultBaseKey));

			DefaultTargetMode = TargetMode.Global;

			ParameterRegistry.Set("average_span", averageSpan, typeof(int));
			ParameterRegistry.Set("shared_result_base_key", sharedResultBaseKey, typeof(string));
			ParameterRegistry.Set("last_running_times", new LinkedList<long>());
			ParameterRegistry.Set("remove_extremas", removeExtremas, typeof(bool));
		}

		/// <summary>
		/// Invoke this hook with a certain parameter registry if optional conditional criteria are satisfied.
		/// </summary>
		/// <param name="registry">The registry containing the required values for this hook's execution.</param>
		/// <param name="resolver">A helper resolver for complex registry entries (automatically cached).</param>
		public override void SubInvoke(IRegistry registry, IRegistryResolver resolver)
		{
			if (ParameterRegistry.ContainsKey("last_time"))
			{
				bool removeExtremas = ParameterRegistry.Get<bool>("remove_extremas");
				long lastTime = ParameterRegistry.Get<long>("last_time");
				long currentTime = Operator.RunningTimeMilliseconds;
				long elapsedTime = currentTime - lastTime;

				LinkedList<long> lastRunningTimes = ParameterRegistry.Get<LinkedList<long>>("last_running_times");
				string sharedResultBaseKey = ParameterRegistry.Get<string>("shared_result_base_key");
				int averageSpan = ParameterRegistry.Get<int>("average_span");

				lastRunningTimes.AddLast(elapsedTime);

				int numberRunningTimes = lastRunningTimes.Count;

				if (numberRunningTimes > averageSpan)
				{
					lastRunningTimes.RemoveFirst();
					numberRunningTimes--;
				}

				long averageTime = lastRunningTimes.Sum();

				if (removeExtremas)
				{
					LinkedList<long> runningTimesCopy = new LinkedList<long>(lastRunningTimes);
					int timesToRemove = (int) Math.Sqrt(lastRunningTimes.Count / 2.0f); // TODO magic number

					while (timesToRemove-- > 0)
					{
						long removedTime = timesToRemove % 2 == 0 ? runningTimesCopy.Max() : runningTimesCopy.Min();

						runningTimesCopy.Remove(removedTime);
					}

					averageTime = runningTimesCopy.Sum();
					numberRunningTimes = runningTimesCopy.Count;
				}

				averageTime /= numberRunningTimes;

				resolver.ResolveSet(sharedResultBaseKey + "_last", elapsedTime, addIdentifierIfNotExists: true);
				resolver.ResolveSet(sharedResultBaseKey + "_average", averageTime, addIdentifierIfNotExists: true);
				resolver.ResolveSet(sharedResultBaseKey + "_min", lastRunningTimes.Min(), addIdentifierIfNotExists: true);
				resolver.ResolveSet(sharedResultBaseKey + "_max", lastRunningTimes.Max(), addIdentifierIfNotExists: true);
			}

			ParameterRegistry["last_time"] = Operator.RunningTimeMilliseconds;
		}
	}
}
