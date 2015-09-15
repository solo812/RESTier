﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OData.Edm;

namespace Microsoft.Restier.Core
{
    /// <summary>
    /// Represents a configuration that defines a domain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A domain configuration defines the model and behavior of a domain
    /// through a set of registered hook points. It also maintains a set of
    /// properties that can be used to share static data between hook points.
    /// </para>
    /// <para>
    /// Hook points may be singletons, meaning there is at most one instance of
    /// the hook point registered, or multi-cast, in which case there can be
    /// zero or more instances of the hook point that are registered. In the
    /// multi-cast case, registration order is maintained, and such hook points
    /// are normally used in the original or reverse order of registration.
    /// </para>
    /// <para>
    /// In order to use a domain configuration, it must first be committed.
    /// This fixes the configuration so that its set of hook points are
    /// immutable, ensuring that any active use of the configuration sees a
    /// consistent set of hook points throughout a particular domain flow.
    /// </para>
    /// <para>
    /// A domain configuration is intended to be long-lived, and can be
    /// statically cached according to a domain configuration key specified
    /// when the configuration is created. Additionally, the domain model
    /// produced as a result of a particular configuration is cached under
    /// the same key to avoid re-computing it on each invocation.
    /// </para>
    /// </remarks>
    public class DomainConfiguration : PropertyBag
    {
        private static readonly IDictionary<object, DomainConfiguration> Configurations =
            new ConcurrentDictionary<object, DomainConfiguration>();

        private readonly IDictionary<Type, object> singletons =
            new Dictionary<Type, object>();

        private readonly IDictionary<Type, IList<object>> multiCasts =
            new Dictionary<Type, IList<object>>();

        private readonly IDictionary<Type, object> hookHandlers = new ConcurrentDictionary<Type, object>();

        /// <summary>
        /// Initializes a new instance of the <see cref="DomainConfiguration" /> class.
        /// </summary>
        public DomainConfiguration()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DomainConfiguration" /> class
        /// that is based on an existing configuration.
        /// </summary>
        /// <param name="key">
        /// A domain configuration key.
        /// </param>
        public DomainConfiguration(object key)
        {
            this.Key = key;
            if (key != null)
            {
                DomainConfiguration.Configurations[key] = this;
            }
        }

        /// <summary>
        /// Gets the domain configuration key, if any.
        /// </summary>
        public object Key { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this domain configuration has been committed.
        /// </summary>
        public bool IsCommitted { get; private set; }

        internal IEdmModel Model { get; set; }

        /// <summary>
        /// Gets an existing domain configuration from a key.
        /// </summary>
        /// <param name="key">
        /// A key.
        /// </param>
        /// <returns>
        /// The existing domain configuration, or <c>null</c> if
        /// no configuration with the key was previously created.
        /// </returns>
        public static DomainConfiguration FromKey(object key)
        {
            Ensure.NotNull(key, "key");
            DomainConfiguration configuration = null;
            DomainConfiguration.Configurations
                .TryGetValue(key, out configuration);
            return configuration;
        }

        /// <summary>
        /// Invalidates an existing domain configuration given a key.
        /// </summary>
        /// <param name="key">
        /// A key.
        /// </param>
        public static void Invalidate(object key)
        {
            Ensure.NotNull(key, "key");
            DomainConfiguration.Configurations.Remove(key);
        }

        /// <summary>
        /// Ensures this domain configuration has been committed.
        /// </summary>
        public void EnsureCommitted()
        {
            if (!this.IsCommitted)
            {
                this.IsCommitted = true;
            }
        }

        /// <summary>
        /// Indicates if this domain configuration has
        /// an instance of a type of singleton hook point.
        /// </summary>
        /// <param name="hookPointType">
        /// The type of a singleton hook point.
        /// </param>
        /// <returns>
        /// <c>true</c> if this domain configuration has an instance of the
        /// specified type of singleton hook point; otherwise, <c>false</c>.
        /// </returns>
        public bool HasHookPoint(Type hookPointType)
        {
            Ensure.NotNull(hookPointType, "hookPointType");
            return this.singletons.ContainsKey(hookPointType);
        }

        /// <summary>
        /// Gets the single instance of a type of singleton hook point.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the singleton hook point.
        /// </typeparam>
        /// <returns>
        /// The single instance of the specified type of singleton hook
        /// point, or <c>null</c> if this domain configuration does not
        /// have an instance of the specified type of singleton hook point.
        /// </returns>
        public T GetHookPoint<T>()
            where T : class
        {
            return (T)this.GetHookPoint(typeof(T));
        }

        /// <summary>
        /// Sets the single instance of a type a singleton hook point.
        /// </summary>
        /// <param name="hookPointType">
        /// The type of a singleton hook point.
        /// </param>
        /// <param name="instance">
        /// The single instance of the specified type of singleton hook point.
        /// </param>
        public void SetHookPoint(Type hookPointType, object instance)
        {
            if (this.IsCommitted)
            {
                throw new InvalidOperationException();
            }

            Ensure.NotNull(hookPointType, "hookPointType");
            Ensure.NotNull(instance, "instance");
            if (!hookPointType.IsAssignableFrom(instance.GetType()))
            {
                // TODO GitHubIssue#24 : error message
                throw new ArgumentException();
            }

            this.singletons[hookPointType] = instance;
        }

        /// <summary>
        /// Indicates if this domain configuration has any
        /// instances of a type of multi-cast hook point.
        /// </summary>
        /// <param name="hookPointType">
        /// The type of a multi-cast hook point.
        /// </param>
        /// <returns>
        /// <c>true</c> if this domain configuration has any instances of the
        /// specified type of multi-cast hook point; otherwise, <c>false</c>.
        /// </returns>
        public bool HasHookPoints(Type hookPointType)
        {
            Ensure.NotNull(hookPointType, "hookPointType");
            return this.multiCasts.ContainsKey(hookPointType);
        }

        /// <summary>
        /// Gets all instances of a type of multi-cast hook point.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the multi-cast hook point.
        /// </typeparam>
        /// <returns>
        /// All instances of the specified type of multi-cast
        /// hook point in the original order of registration.
        /// </returns>
        public IEnumerable<T> GetHookPoints<T>()
            where T : class
        {
            return this.GetHookPoints(typeof(T)).Cast<T>();
        }

        /// <summary>
        /// Adds an instance of a type of multi-cast hook point.
        /// </summary>
        /// <param name="hookPointType">
        /// The type of a multi-cast hook point.
        /// </param>
        /// <param name="instance">
        /// An instance of the type of multi-cast hook point.
        /// </param>
        public void AddHookPoint(Type hookPointType, object instance)
        {
            if (this.IsCommitted)
            {
                throw new InvalidOperationException();
            }

            Ensure.NotNull(hookPointType, "hookPointType");
            Ensure.NotNull(instance, "instance");
            if (!hookPointType.IsAssignableFrom(instance.GetType()))
            {
                // TODO GitHubIssue#24 : error message
                throw new ArgumentException();
            }

            IList<object> instances = null;
            if (!this.multiCasts.TryGetValue(hookPointType, out instances))
            {
                instances = new List<object>();
                this.multiCasts.Add(hookPointType, instances);
            }

            instances.Add(instance);
        }

        #region HookHandler
        /// <summary>
        /// Add an hook handler instance.
        /// </summary>
        /// <typeparam name="T">The context class.</typeparam>
        /// <param name="handler">An instance of hook handler for TContext.</param>
        /// <returns>Current <see cref="DomainConfiguration"/></returns>
        public DomainConfiguration AddHookHandler<T>(T handler) where T : class, IHookHandler
        {
            Ensure.NotNull(handler, "handler");

            if (this.IsCommitted)
            {
                throw new InvalidOperationException();
            }

            if (!typeof(T).IsInterface)
            {
                throw new InvalidOperationException("Should specify an interface type T for the handler.");
            }

            var delegateHandler = handler as IDelegateHookHandler<T>;
            if (delegateHandler != null)
            {
                delegateHandler.InnerHandler = this.GetHookHandler<T>();
            }

            this.hookHandlers[typeof(T)] = handler;
            return this;
        }

        internal T GetHookHandler<T>() where T : class, IHookHandler
        {
            object value;
            this.hookHandlers.TryGetValue(typeof(T), out value);
            return value as T;
        }
        #endregion

        private object GetHookPoint(Type hookPointType)
        {
            object instance = null;
            this.singletons.TryGetValue(hookPointType, out instance);
            return instance;
        }

        private IEnumerable<object> GetHookPoints(Type hookPointType)
        {
            IList<object> list;
            this.multiCasts.TryGetValue(hookPointType, out list);
            return list ?? Enumerable.Empty<object>();
        }
    }
}
