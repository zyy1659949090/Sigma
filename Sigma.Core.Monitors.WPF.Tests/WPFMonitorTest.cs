﻿using NUnit.Framework;
using System.Threading;

namespace Sigma.Core.Monitors.WPF.Tests
{
	public class WPFMonitorTest
	{
		private SigmaEnvironment ClearAndCreate(string identifier)
		{
			SigmaEnvironment.Clear();

			return SigmaEnvironment.Create(identifier);
		}

		[TestCase]
		public void TestWPFMonitorCreation()
		{
			SigmaEnvironment sigma = ClearAndCreate("Test");

			WPFMonitor monitor = sigma.AddMonitor(new WPFMonitor("Sigma GUI Demo"));
			monitor.Priority = ThreadPriority.Lowest;

			Assert.AreSame(sigma, monitor.Sigma);
			Assert.AreEqual(monitor.Priority, ThreadPriority.Lowest);
			Assert.AreEqual(monitor.Title, "Sigma GUI Demo");
			Assert.AreNotEqual(monitor.Title, "Sigma GUI Demo2");
		}
	}
}
