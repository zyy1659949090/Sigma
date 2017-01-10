﻿/* 
MIT License

Copyright (c) 2016-2017 Florian Cäsar, Michael Plainer

For full license see LICENSE in the root directory of this project. 
*/

using System;
using System.Collections.Generic;
using log4net;
using static Sigma.Core.Utils.ThreadUtils;
using Sigma.Core.Architecture;
using Sigma.Core.Handlers;
using Sigma.Core.Training.Hooks;
using Sigma.Core.Training.Mergers;
using Sigma.Core.Training.Operators.Workers;
using Sigma.Core.Utils;

namespace Sigma.Core.Training.Operators
{
	public abstract class BaseOperator : IOperator
	{
		/// <summary>
		///     All <see cref="IActiveHook" />s that are attached to this <see cref="IOperator" />.
		/// </summary>
		protected readonly ICollection<IActiveHook> ActiveHooks;

		/// <summary>
		///     All <see cref="IPassiveHook" />s that are attached to this <see cref="IOperator" />.
		/// </summary>
		protected readonly ICollection<IPassiveHook> PassiveHooks;

		/// <summary>
		///		All active hooks sorted by time scale.
		/// </summary>
		protected readonly IDictionary<TimeScale, ISet<IActiveHook>> ActiveHooksByTimeScale;

		/// <summary>
		///		All passive hooks sorted by time scale.
		/// </summary>
		protected readonly IDictionary<TimeScale, ISet<IPassiveHook>> PassiveHooksByTimescale;

		/// <summary>
		///		The time scale countdowns per passive hook (passive hooks are managed by the operator).
		/// </summary>
		protected IDictionary<IHook, int> PassiveHookTimescaleCountdowns;

		/// <summary>
		///     All the <see cref="IWorker" />s managed by this operator.
		/// </summary>
		protected IEnumerable<IWorker> Workers;

		/// <summary>
		///     The <see cref="SigmaEnvironment" /> this operator runs in and communicates with.
		///     It will be automatically set by the <see cref="ITrainer" />.
		/// </summary>
		public SigmaEnvironment Sigma { get; set; }

		/// <summary>
		///     The current <see cref="ExecutionState" /> of the <see cref="IOperator" />. <see cref="ExecutionState.None" />
		///     if the operator has not been started yet.
		/// </summary>
		public ExecutionState State { get; protected set; } = ExecutionState.None;

		/// <summary>
		///     The <see cref="IComputationHandler" /> used to compute everything in
		///     this <see cref="IOperator" />. It will be automatically set by the
		///     <see cref="ITrainer" /> if not specified.
		/// </summary>
		public IComputationHandler Handler { get; set; }

		/// <summary>
		///     The <see cref="ITrainer" /> that is being trained in this operators training process.
		///     This will be automatically set by the corresponding <see cref="ITrainer" />.
		/// </summary>
		public ITrainer Trainer { get; set; }

		/// <summary>
		///     The <see cref="INetwork" /> the training process is operated on.
		///     This will be automatically set by the corresponding <see cref="ITrainer" />.
		/// </summary>
		public INetwork Network { get; set; }

		/// <summary>
		///		This merger is used to merge multiple networks after they get
		///		reported to the <see cref="IOperator"/>.
		/// </summary>
		public INetworkMerger Merger { get; set; }

		/// <summary>
		///     The number of <see cref="IWorker" />s (threads) used in this
		///     <see cref="IOperator" /> in parallel.
		/// </summary>
		public int WorkerCount { get; }

		/// <summary>
		///		The number of the current global epoch in this operator.
		/// </summary>
		public int EpochNumber { get; }

		/// <summary>
		/// The logger, it will be initialised in the property so that the class matches.
		/// </summary>
		private ILog _logger;

		/// <summary>
		/// The logger for the inherited class. 
		/// </summary>
		protected ILog Logger => _logger ?? (_logger = LogManager.GetLogger(GetType()));

		/// <summary>
		/// The lock that will be used to perform asynchronous management of the <see cref="IWorker"/>.
		/// </summary>
		private readonly object _stateChangeLock;

		/// <summary>
		///     Create a new <see cref="BaseOperator" /> without specifying the <see cref="IComputationHandler" />.
		///     The <see cref="IComputationHandler" /> will be automatically set by the <see cref="ITrainer" />.
		/// </summary>
		/// <param name="workerCount">
		///     The number of <see cref="IWorker" />s (threads) used in this <see cref="IOperator" /> in
		///     parallel.
		/// </param>
		protected BaseOperator(int workerCount)
		{
			_stateChangeLock = new object();

			ActiveHooks = new List<IActiveHook>();
			PassiveHooks = new List<IPassiveHook>();
			ActiveHooksByTimeScale = new Dictionary<TimeScale, ISet<IActiveHook>>();
			PassiveHooksByTimescale = new Dictionary<TimeScale, ISet<IPassiveHook>>();
			WorkerCount = workerCount;
			EpochNumber = -1;
		}

		/// <summary>
		///     Create a new <see cref="BaseOperator" /> with a specified <see cref="IComputationHandler" />.
		///     The <see cref="IComputationHandler" /> will <c>not</c> be modified by the <see cref="ITrainer" />.
		/// </summary>
		/// <param name="handler">
		///     The <see cref="IComputationHandler" /> that will be assigned to the
		///     <see cref="IComputationHandler" />
		/// </param>
		/// <param name="workerCount">
		///     The number of <see cref="IWorker" />s (threads) used in this <see cref="IOperator" /> in
		///     parallel.
		/// </param>
		protected BaseOperator(IComputationHandler handler, int workerCount) : this(workerCount)
		{
			Handler = handler;
		}

		public void AttachHook(IActiveHook hook)
		{
			ActiveHooks.Add(hook);

			if (!ActiveHooksByTimeScale.ContainsKey(hook.TimeStep.TimeScale))
			{
				ActiveHooksByTimeScale.Add(hook.TimeStep.TimeScale, new HashSet<IActiveHook>());
			}

			ActiveHooksByTimeScale[hook.TimeStep.TimeScale].Add(hook);

			Logger.Debug($"Attached active hook {hook} to operator {this}.");
		}

		public void DetachHook(IActiveHook hook)
		{
			if (ActiveHooks.Remove(hook))
			{
				ActiveHooksByTimeScale[hook.TimeStep.TimeScale].Remove(hook);

				Logger.Debug($"Detached active hook {hook} from operator {this}");
			}
		}

		public void AttachHook(IPassiveHook hook)
		{
			PassiveHooks.Add(hook);

			if (!PassiveHooksByTimescale.ContainsKey(hook.TimeStep.TimeScale))
			{
				PassiveHooksByTimescale.Add(hook.TimeStep.TimeScale, new HashSet<IPassiveHook>());
			}

			PassiveHooksByTimescale[hook.TimeStep.TimeScale].Add(hook);

			Logger.Debug($"Attached passive hook {hook} to operator {this}.");
		}

		public void DetachHook(IPassiveHook hook)
		{
			if (PassiveHooks.Remove(hook))
			{
				PassiveHooksByTimescale[hook.TimeStep.TimeScale].Remove(hook);

				Logger.Debug($"Detached passive hook {hook} from operator {this}");
			}
		}

		/// <summary>
		/// Invoke hooks for a certain time scale with a certain worker.
		/// </summary>
		/// <param name="timeScale">The time scale.</param>
		/// <param name="worker">The worker to invoke the hook with.</param>
		/// <param name="hooks">The hooks to check and invoke.</param>
		/// <param name="localHookTimeSteps">The local hook time steps to use (and populate if missing).</param>
		public void EjectTimeScaleEvent(TimeScale timeScale, IWorker worker, IEnumerable<IHook> hooks, IDictionary<IHook, TimeStep> localHookTimeSteps)
		{
			Logger.Debug($"Invoking time scale event {timeScale} for worker {worker} in operator {this}...");

			foreach (IHook hook in hooks)
			{
				if (!localHookTimeSteps.ContainsKey(hook))
				{
					TimeStep timeStep = (TimeStep) hook.TimeStep.DeepCopy();

					timeStep.LocalLiveTime = timeStep.LiveTime;
					timeStep.LocalInterval = timeStep.Interval;
				}

				if (hook.TimeStep.LocalLiveTime > 0)
				{
					hook.TimeStep.LocalInterval--;

					if (hook.TimeStep.LocalInterval == 0)
					{
						// TODO invoke hook

						hook.TimeStep.LocalLiveTime--;
					}
				}
			}
		}

		/// <summary>
		/// This method blocks until the last state change has been fully performed.
		/// Returns immediately if not implemented.
		/// </summary>
		public void WaitForStateChanged()
		{
			lock (_stateChangeLock) { }
		}

		/// <summary>
		/// This method assures that <see cref="Workers"/> is initialised (with <see cref="InitialiseWorkers"/>)
		/// and checks if all required parameters are set. 
		/// </summary>
		protected virtual void PrepareWorkers()
		{
			// TODO: check if all required parameter are set

			if (Workers == null) { Workers = InitialiseWorkers(); }
		}

		/// <summary>
		///     This method creates the <see cref="IWorker" />s. It will be called before the first start of the operator.
		///     The <see cref="IWorker" />s are usually created via <see cref="CreateWorker" />.
		/// </summary>
		/// <returns>An <see cref="IEnumerable{T}" /> with the required amount of <see cref="IWorker" />s.</returns>
		protected virtual IEnumerable<IWorker> InitialiseWorkers()
		{
			IWorker[] workers = new IWorker[WorkerCount];

			for (int i = 0; i < workers.Length; i++) { workers[i] = CreateWorker(); }

			return workers;
		}

		/// <summary>
		/// Start all workers with <see cref="StartWorker"/>.
		/// </summary>
		protected virtual void StartWorkers()
		{
			foreach (IWorker worker in Workers) { StartWorker(worker); }
		}

		/// <summary>
		///		Start all workers once (for one iteration) with <see cref="RunWorkerOnce"/>. 
		/// </summary>
		protected virtual void StartWorkersOnce()
		{
			foreach (IWorker worker in Workers) { RunWorkerOnce(worker); }
		}

		#region StateControl

		public virtual void StartOnce()
		{
			if ((State == ExecutionState.None) || (State == ExecutionState.Stopped))
			{
				new BlockingLockingThread(_stateChangeLock, () =>
				{
					PrepareWorkers();

					StartWorkersOnce();

					State = ExecutionState.Running;
				}).Start();
			}
			else
			{
				ThrowBadState("started");
			}
		}

		/// <summary>
		///     Start this operator in a separate thread (return immediately).
		/// </summary>
		/// <exception cref="InvalidOperationException">If the operator is running or paused.</exception>
		public void Start()
		{
			if ((State == ExecutionState.None) || (State == ExecutionState.Stopped))
			{
				new BlockingLockingThread(_stateChangeLock, () =>
				{
					PrepareWorkers();

					StartWorkers();

					State = ExecutionState.Running;
				}).Start();
			}
			else
			{
				ThrowBadState("started");
			}
		}

		/// <summary>
		///     Signal this operator to stop as soon as possible.
		/// </summary>
		/// <exception cref="InvalidOperationException">If the operator is not running.</exception>
		public void SignalPause()
		{
			if (State == ExecutionState.Running)
			{
				new BlockingLockingThread(_stateChangeLock, () =>
				{
					foreach (IWorker worker in Workers) { PauseWorker(worker); }

					State = ExecutionState.Paused;
				}).Start();
			}
			else
			{
				ThrowBadState("paused");
			}
		}

		/// <summary>
		///     Signal this operator to resume as soon as possible.
		/// </summary>
		/// <exception cref="InvalidOperationException">If the operator is not paused.</exception>
		public void SignalResume()
		{
			if (State == ExecutionState.Paused)
			{
				new BlockingLockingThread(_stateChangeLock, () =>
				 {
					 foreach (IWorker worker in Workers) { ResumeWorker(worker); }

					 State = ExecutionState.Running;
				 }).Start();
			}
			else
			{
				ThrowBadState("resumed");
			}
		}

		/// <summary>
		///     Signal this operator to stop as soon as possible.
		/// </summary>
		/// <exception cref="InvalidOperationException">If the operator is already stopped.</exception>
		public void SignalStop()
		{
			if (State != ExecutionState.Stopped)
			{
				new BlockingLockingThread(_stateChangeLock, () =>
				 {
					 foreach (IWorker worker in Workers)
					 {
						 PauseWorker(worker);
						 StopWorker(worker);
					 }

					 State = ExecutionState.Stopped;
				 }).Start();
			}
			else
			{
				ThrowBadState("stopped");
			}
		}

		/// <summary>
		/// </summary>
		/// <param name="currentState"></param>
		/// <exception cref="InvalidOperationException"></exception>
		private void ThrowBadState(string currentState)
		{
			throw new InvalidOperationException($"The operator cannot be {currentState} because the state is: {State}!");
		}

		#endregion

		public abstract void PushProgress(IWorker worker);
		public abstract void PullProgress(IWorker worker);

		#region AbstractWorkerMethods

		/// <summary>
		///     This method creates an <see cref="IWorker" />.
		/// </summary>
		/// <returns>The newly created <see cref="IWorker" />.</returns>
		protected abstract IWorker CreateWorker();

		/// <summary>
		///     This method starts a worker.
		/// </summary>
		/// <param name="worker">The worker that will be started.</param>
		protected abstract void StartWorker(IWorker worker);

		/// <summary>
		///     This method starts a worker for a single iteration.
		/// </summary>
		/// <param name="worker">The worker that will be started.</param>
		protected abstract void RunWorkerOnce(IWorker worker);

		/// <summary>
		///     This method pauses a worker. It will also be
		///     called if the worker is stopped.
		/// </summary>
		/// <param name="worker">The worker that will be paused.</param>
		protected abstract void PauseWorker(IWorker worker);

		/// <summary>
		///     This method resumes a worker from it's paused state.
		/// </summary>
		/// <param name="worker">The worker that will be resumed.</param>
		protected abstract void ResumeWorker(IWorker worker);

		/// <summary>
		///     This method stops a worker. All resources should
		///     be freed.
		/// </summary>
		/// <param name="worker">The worker that will be paused and stopped.</param>
		protected abstract void StopWorker(IWorker worker);

		#endregion AbstractWorkerMethods
	}
}