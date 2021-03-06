﻿// Copyright (c) Simple Injector Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for license information.

namespace SimpleInjector.Internals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using SimpleInjector;
    using SimpleInjector.Decorators;

    internal abstract class CollectionResolver
    {
        private readonly List<RegistrationGroup> registrationGroups = new List<RegistrationGroup>();

        private readonly Dictionary<Type, InstanceProducer> producerCache =
            new Dictionary<Type, InstanceProducer>();

        private bool verified;

        protected CollectionResolver(Container container, Type serviceType)
        {
            Requires.IsNotPartiallyClosed(serviceType, nameof(serviceType));

            this.Container = container;
            this.ServiceType = serviceType;
        }

        protected IEnumerable<RegistrationGroup> RegistrationGroups => this.registrationGroups;

        protected Type ServiceType { get; }

        protected Container Container { get; }

        internal abstract void AddControlledRegistrations(
            Type serviceType, ContainerControlledItem[] items, bool append);

        internal abstract void RegisterUncontrolledCollection(Type serviceType, InstanceProducer producer);

        internal InstanceProducer? TryGetInstanceProducer(Type elementType) =>
            this.ServiceType == elementType || this.ServiceType.IsGenericTypeDefinitionOf(elementType)
                ? this.GetInstanceProducerFromCache(elementType)
                : null;

        internal void ResolveUnregisteredType(object sender, UnregisteredTypeEventArgs e)
        {
            if (typeof(IEnumerable<>).IsGenericTypeDefinitionOf(e.UnregisteredServiceType))
            {
                Type closedServiceType = e.UnregisteredServiceType.GetGenericArguments().Single();

                if (this.ServiceType.IsGenericTypeDefinitionOf(closedServiceType))
                {
                    var producer = this.GetInstanceProducerFromCache(closedServiceType);

                    if (producer != null)
                    {
                        e.Register(producer.Registration);
                    }
                }
            }
        }

        internal void TriggerUnregisteredTypeResolutionOnAllClosedCollections()
        {
            if (!this.verified)
            {
                this.verified = true;

                foreach (Type closedServiceType in this.GetAllKnownClosedServiceTypes())
                {
                    // When registering a generic collection, the container keeps track of all open and closed
                    // elements in the resolver. This resolver allows unregistered type resolution and this
                    // allows all closed versions of the collection to be resolved. But if we only used
                    // unregistered type resolution, this could cause these registrations to be hidden from
                    // the verification mechanism in case the collections are root types in the application.
                    // This could cause the container to verify, while still failing at runtime when resolving
                    // a collection. So by explicitly resolving the known closed-generic versions here, we
                    // ensure that all non-generic registrations (and because of that, most open-generic
                    // registrations as well) will be validated.
                    this.Container.GetRegistration(typeof(IEnumerable<>).MakeGenericType(closedServiceType));
                }
            }
        }

        protected abstract Type[] GetAllKnownClosedServiceTypes();

        protected abstract InstanceProducer BuildCollectionProducer(Type closedServiceType);

        protected void AddRegistrationGroup(RegistrationGroup group)
        {
            if (!group.Appended)
            {
                if (this.Container.Options.AllowOverridingRegistrations)
                {
                    this.RemoveRegistrationsToOverride(group.ServiceType);
                }

                this.CheckForOverlappingRegistrations(group.ServiceType);
            }

            this.registrationGroups.Add(group);
        }

        private InstanceProducer GetInstanceProducerFromCache(Type closedServiceType)
        {
            lock (this.producerCache)
            {
                InstanceProducer producer;

                if (!this.producerCache.TryGetValue(closedServiceType, out producer))
                {
                    this.producerCache[closedServiceType] =
                        producer = this.BuildCollectionProducer(closedServiceType);
                }

                return producer;
            }
        }

        private void RemoveRegistrationsToOverride(Type serviceType)
        {
            this.registrationGroups.RemoveAll(group => group.ServiceType == serviceType || group.Appended);
        }

        private void CheckForOverlappingRegistrations(Type serviceType)
        {
            var overlappingGroups = this.GetOverlappingGroupsFor(serviceType);

            if (overlappingGroups.Any())
            {
                if (!serviceType.ContainsGenericParameters() &&
                    overlappingGroups.Any(group => group.ServiceType == serviceType))
                {
                    throw new InvalidOperationException(
                        StringResources.CollectionTypeAlreadyRegistered(serviceType));
                }

                throw new InvalidOperationException(
                    StringResources.MixingCallsToCollectionsRegisterIsNotSupported(serviceType));
            }
        }

        private IEnumerable<RegistrationGroup> GetOverlappingGroupsFor(Type serviceType) =>
            from registrationGroup in this.RegistrationGroups
            where !registrationGroup.Appended
            where registrationGroup.ServiceType == serviceType
                || serviceType.ContainsGenericParameters()
                || registrationGroup.ServiceType.ContainsGenericParameters()
            select registrationGroup;

        protected sealed class RegistrationGroup
        {
            private RegistrationGroup(Type serviceType, bool appended)
            {
                this.ServiceType = serviceType;
                this.Appended = appended;
            }

            internal Type ServiceType { get; }

            internal IEnumerable<ContainerControlledItem>? ControlledItems { get; private set; }

            internal InstanceProducer? UncontrolledProducer { get; private set; }

            internal bool Appended { get; }

            internal static RegistrationGroup CreateForUncontrolledProducer(Type serviceType,
                InstanceProducer producer) =>
                new RegistrationGroup(serviceType, appended: false)
                {
                    UncontrolledProducer = producer
                };

            internal static RegistrationGroup CreateForControlledItems(
                Type serviceType, ContainerControlledItem[] items, bool appended) =>
                new RegistrationGroup(serviceType, appended)
                {
                    ControlledItems = items,
                };
        }
    }
}