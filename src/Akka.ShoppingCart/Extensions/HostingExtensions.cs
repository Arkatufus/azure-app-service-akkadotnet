﻿// -----------------------------------------------------------------------
//  <copyright file="HostingExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//      Copyright (c) Microsoft. All rights reserved.
//      Licensed under the MIT License.
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.ShoppingCart.Extensions;

public static class HostingExtensions
{
    public static AkkaConfigurationBuilder AddShoppingCartRegions(this AkkaConfigurationBuilder builder)
    {
        return builder
            .WithShardRegion<RegistryKey.ProductRegion>(
                typeName: "product",
                entityPropsFactory: ProductActor.Props,
                extractEntityId: Product.ExtractEntityId,
                extractShardId: Product.ExtractShardId,
                shardOptions: new ShardOptions())
            .WithShardRegion<RegistryKey.InventoryRegion>(
                typeName: "inventory",
                entityPropsFactory: InventoryActor.Props,
                extractEntityId: Inventory.ExtractEntityId,
                extractShardId: Inventory.ExtractShardId,
                shardOptions: new ShardOptions())
            .WithShardRegion<RegistryKey.ShoppingCartRegion>(
                typeName: "shoppingCart",
                entityPropsFactory: ShoppingCartActor.Props,
                extractEntityId: Messages.ShoppingCart.ExtractEntityId,
                extractShardId: Messages.ShoppingCart.ExtractShardId,
                shardOptions: new ShardOptions());
    }

    // TODO: Move this to Akka.Hosting in the future.
    public static AkkaConfigurationBuilder WithAzureDiscovery(
        this AkkaConfigurationBuilder builder,
        Action<AzureDiscoverySetup> configure)
    {
        builder.AddHocon(
            ((Config)"akka.discovery.method = azure").WithFallback(AzureServiceDiscovery.DefaultConfig), 
            HoconAddMode.Prepend);

        var setup = new AzureDiscoverySetup();
        configure(setup);
        builder.AddSetup(setup);
        return builder;
    }

    // TODO: Move this to Akka.Hosting in the future.
    public static AkkaConfigurationBuilder WithExtensions(
        this AkkaConfigurationBuilder builder,
        params Type[] extensions)
    {
        var config = (Config)$"akka.extensions=[{string.Join(", ", extensions.Select(s => $"\"{s.AssemblyQualifiedName}\""))}]";
        builder.AddHocon(config, HoconAddMode.Prepend);
        return builder;
    }

    // TODO: Move this to Akka.Hosting in the future.
    public static AkkaConfigurationBuilder WithAkkaManagement(
        this AkkaConfigurationBuilder builder,
        Action<AkkaManagementSetup> configure,
        bool autoStart = false)
    {
        var setup = new AkkaManagementSetup
        {
            Http = new HttpSetup()
        };
        configure(setup);
        builder.AddSetup(setup);
        if (autoStart)
        {
            builder.StartActors(async (system, _) =>
            {
                await AkkaManagement.Get(system).Start();
            });
        }

        return builder;
    }

    // TODO: Move this to Akka.Hosting in the future.
    public static AkkaConfigurationBuilder WithClusterBootstrap(
        this AkkaConfigurationBuilder builder,
        Action<ClusterBootstrapSetup> configure,
        bool autoStart = true)
    {
        if (autoStart)
            builder.WithExtensions(typeof(ClusterBootstrap));
        
        var setup = new ClusterBootstrapSetup();
        configure(setup);
        builder.AddSetup(setup);
        
        return builder;
    }
}