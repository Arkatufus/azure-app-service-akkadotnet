﻿// -----------------------------------------------------------------------
//  <copyright file="InventoryActor.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//      Copyright (c) Microsoft
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.ShoppingCart.Actors;

public class InventoryActor: ReceivePersistentActor
{
    public static Props Props(string categoryId)
        => Actor.Props.Create(() => new InventoryActor(categoryId));
    
    private HashSet<string> _productIds = new();
    private readonly Dictionary<string, ProductDetails> _productCache = new();
    private readonly IActorRef _productRegion;

    public override string PersistenceId { get; }

    public InventoryActor(string categoryId)
    {
        PersistenceId = categoryId;
        
        var resolver = DependencyResolver.For(Context.System);
        var registry = resolver.Resolver.GetService<ActorRegistry>();
        _productRegion = registry.Get<RegistryKey.ProductRegion>();
        
        Recover<SnapshotOffer>(msg =>
        {
            _productIds = (HashSet<string>) msg.Snapshot;

            Parallel.ForEachAsync(
                _productIds,
                new ParallelOptions { TaskScheduler = TaskScheduler.Current },
                async (id, token) =>
                {
                    _productCache[id] = await _productRegion.Ask<ProductDetails>(new Product.GetDetails(id), token);
                }).PipeTo(Self, success: () => Done.Instance);
        });
        Recover<Inventory.AddOrUpdateProduct>(UpdateState);
        Recover<Inventory.RemoveProduct>(UpdateState);

        Command<Inventory.GetAllProducts>(_ =>
        {
            Sender.Tell(_productCache.Values.ToHashSet());
        });
        
        Command<Inventory.AddOrUpdateProduct>(msg =>
        {
            Persist(msg, updateProduct =>
            {
                UpdateState(updateProduct);
                SaveSnapshot(_productIds);
            });
        });

        Command<Inventory.RemoveProduct>(msg =>
        {
            Persist(msg, product =>
            {
                UpdateState(product);
                SaveSnapshot(_productIds);
            });
        });
    }

    protected override bool Receive(object message)
    {
        switch (message)
        {
            case Done:
                return true;
            case Status.Failure fail:
                throw new Exception("Failed to re-initialize cache.", fail.Cause);
            case SaveSnapshotSuccess:
                return true;
        }
        return base.Receive(message);
    }

    private void UpdateState(object message)
    {
        switch (message)
        {
            case Inventory.AddOrUpdateProduct msg:
            {
                var product = msg.ProductDetails;
                _productIds.Add(product.Id);
                _productCache[product.Id] = product;
                break;
            }
            case Inventory.RemoveProduct msg:
            {
                var productId = msg.ProductId;
                _productIds.Remove(productId);
                _productCache.Remove(productId);
                break;
            }
        }
    }
}