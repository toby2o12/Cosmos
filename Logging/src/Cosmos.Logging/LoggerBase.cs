﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Cosmos.Logging.Configurations;
using Cosmos.Logging.Core;
using Cosmos.Logging.Core.Callers;
using Cosmos.Logging.Core.ObjectResolving;
using Cosmos.Logging.Core.Payloads;
using Cosmos.Logging.Events;
using Cosmos.Logging.Filters.Internals;
using Cosmos.Logging.Future;

namespace Cosmos.Logging {
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public abstract partial class LoggerBase : ILogger, IDisposable {
        private readonly ILogPayloadSender _logPayloadSender;
        private readonly MessageParameterProcessor _messageParameterProcessor;
        private readonly Func<string, LogEventLevel, bool> _filter;
        private static readonly Func<string, LogEventLevel, bool> TrueFilter = (s, l) => true;
        private readonly RendingConfiguration _upstreamRenderingOptions;
        private long CurrentManuallyTransId { get; set; }

        protected LoggerBase(
            Type sourceType,
            LogEventLevel minimumLevel,
            string loggerStateNamespace,
            Func<string, LogEventLevel, bool> filter,
            LogEventSendMode sendMode,
            RendingConfiguration renderingOptions,
            ILogPayloadSender logPayloadSender) {
            StateNamespace = loggerStateNamespace;
            TargetType = sourceType ?? typeof(object);
            MinimumLevel = minimumLevel;
            SendMode = sendMode;
            _filter = filter ?? TrueFilter;
            _logPayloadSender = logPayloadSender ?? throw new ArgumentNullException(nameof(logPayloadSender));
            _messageParameterProcessor = MessageParameterProcessorCache.Get();
            _upstreamRenderingOptions = renderingOptions ?? new RendingConfiguration();

            AutomaticPayload = new LogPayload(sourceType, loggerStateNamespace, Enumerable.Empty<LogEvent>());
            ManuallyPayload = new LogPayload(sourceType, loggerStateNamespace, Enumerable.Empty<LogEvent>());
            CurrentManuallyTransId = DateTime.Now.Ticks;
            _manuallyLogEventDescriptors.TryAdd(CurrentManuallyTransId, new List<ManuallyLogEventDescriptor>());
        }

        public string StateNamespace { get; }
        public Type TargetType { get; }
        public LogEventLevel MinimumLevel { get; }
        public LogEventSendMode SendMode { get; }

        private readonly ILogPayload AutomaticPayload;
        private readonly ILogPayload ManuallyPayload;

        public bool IsEnabled(LogEventLevel level) {
            return _filter(StateNamespace, level) && PreliminaryEventPercolator.Percolate(level, this);
        }

        protected virtual bool IsManuallySendMode(LogEventSendMode modeInEvent) =>
            modeInEvent != LogEventSendMode.Automatic &&
            (SendMode == LogEventSendMode.Customize && modeInEvent == LogEventSendMode.Manually || SendMode == LogEventSendMode.Manually);

        protected bool IsManuallySendMode(LogEvent logEvent) => IsManuallySendMode(logEvent?.SendMode ?? LogEventSendMode.Customize);

        public void Write(LogEventId eventId, LogEventLevel level, Exception exception, string messageTemplate, LogEventSendMode sendMode, ILogCallerInfo callerInfo,
            LogEventContext context = null, params object[] messageTemplateParameters) {
            if (!IsEnabled(level)) return;
            if (string.IsNullOrWhiteSpace(messageTemplate)) return;
            if (IsManuallySendMode(sendMode)) {
                ParseAndInsertLogEvenDescriptorManually(eventId ?? new LogEventId(), level, exception, messageTemplate, callerInfo, context, messageTemplateParameters);
            } else {
                ParseAndInsertLogEventIntoQueueAutomatically(eventId ?? new LogEventId(), level, exception, messageTemplate, callerInfo, context, messageTemplateParameters);
            }
        }

        public void Write(LogEvent logEvent) {
            if (logEvent == null || !IsEnabled(logEvent.Level)) return;
            Dispatch(logEvent);
        }

        public void Write(FutureLogEventDescriptor descriptor) {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (!IsEnabled(descriptor.Level)) return;
            if (string.IsNullOrWhiteSpace(descriptor.MessageTemplate)) return;
            Write(
                descriptor.EventId,
                descriptor.Level,
                descriptor.Exception,
                descriptor.MessageTemplate,
                LogEventSendMode.Automatic,
                descriptor.CallerInfo,
                descriptor.Context,
                descriptor.Context.Parameters);
        }

        protected virtual void Dispatch(LogEvent logEvent) {
            if (IsManuallySendMode(logEvent)) {
                ManuallyPayload.Add(logEvent);
            } else {
                AutomaticPayload.Add(logEvent);
                AutomaticalSubmitLoggerByPipleline();
            }
        }

        public IDisposable BeginScope<TState>(TState state) {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return LoggingScope.Push(StateNamespace, state);
        }

        public void SubmitLogger() {
            SubmitLogEventsManually(new DisposableAction(() => {
                CurrentManuallyTransId = DateTime.Now.Ticks;
                _manuallyLogEventDescriptors.Clear();
                _manuallyLogEventDescriptors.TryAdd(CurrentManuallyTransId, new List<ManuallyLogEventDescriptor>());
            }));
        }

        private static LogEventContext TouchLogEventContext(Action<LogEventContext> additionalOptContextAct) {
            var context = new LogEventContext();
            additionalOptContextAct?.Invoke(context);
            return context;
        }

        public abstract IFutureLogger ToFuture([CallerMemberName] string memberName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0);

        #region Dispose

        private bool disposed;

        public void Dispose() {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing) {
            if (disposed) return;

            if (disposing) {
                _automaticAsyncQueue.Dispose();
            }

            disposed = true;
        }

        #endregion

    }
}