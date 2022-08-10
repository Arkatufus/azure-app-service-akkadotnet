﻿// -----------------------------------------------------------------------
//  <copyright file="ShoppingCartActor.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//      Copyright (c) Microsoft. All rights reserved.
//      Licensed under the MIT License.
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.ShoppingCart.Actors;

public class ShoppingCartActor: ReceivePersistentActor
{
    public static Props Props(string userId)
        => Actor.Props.Create(() => new ShoppingCartActor(userId));

    private ImmutableDictionary<string, CartItem> _cart = ImmutableDictionary<string, CartItem>.Empty;
    private readonly IActorRef _productRegion;

    public override string PersistenceId { get; }

    public ShoppingCartActor(string userId)
    {
        PersistenceId = userId;

        var resolver = DependencyResolver.For(Context.System);
        var registry = resolver.Resolver.GetService<ActorRegistry>();
        _productRegion = registry.Get<RegistryKey.ProductRegion>();

        #region Recovery

        Recover<SnapshotOffer>(msg =>
        {
            var oldCart = (ImmutableDictionary<string, CartItem>)msg.Snapshot;
            var builder = ImmutableDictionary.CreateBuilder<string, CartItem>();
            foreach (var kvp in oldCart)
            {
                builder[kvp.Key] = kvp.Value;
            }
            _cart = builder.ToImmutable();
        });
        
        Recover<Messages.ShoppingCart.AddOrUpdateItem>(msg =>
        {
            _cart = _cart.SetItem(msg.Product.Id, ToCartItem(msg.Quantity, msg.Product));
        });

        Recover<Messages.ShoppingCart.EmptyCart>(msg =>
        {
            _cart = ImmutableDictionary<string, CartItem>.Empty;
        });

        Recover<Messages.ShoppingCart.RemoveItem>(msg =>
        {
            _cart = _cart.Remove(msg.Product.Id);
        });
        #endregion
        
        CommandAsync<Messages.ShoppingCart.AddOrUpdateItem>(async message =>
        {
            var sender = Sender;
            var product = message.Product;
            var quantity = message.Quantity;
            
            int? adjustedQuantity = null;

            if (_cart.TryGetValue(product.Id, out var existingItem))
            {
                adjustedQuantity = quantity - existingItem.Quantity;
            }

            var (isAvailable, claimedProduct) = await _productRegion.Ask<Product.TakeResult>(
                new Product.TryTake(product.Id, adjustedQuantity ?? quantity));

            if (isAvailable && claimedProduct is not null)
            {
                var persisted =
                    new Messages.ShoppingCart.AddOrUpdateItem(PersistenceId, quantity, claimedProduct);
                Persist(persisted, msg =>
                {
                    _cart = _cart.SetItem(claimedProduct.Id, ToCartItem(msg.Quantity, msg.Product));
                    SaveSnapshot(_cart);
                    sender.Tell(true);
                });
            }
            else
            {
                sender.Tell(false);
            }
        });
        
        CommandAsync<Messages.ShoppingCart.EmptyCart>(async _ =>
        {
            var sender = Sender;
            foreach (var item in _cart.Values)
            {
                await _productRegion.Ask<Done>(new Product.Return(item.Product.Id, item.Quantity));
            }
            _cart = ImmutableDictionary<string, CartItem>.Empty;
            sender.Tell(Done.Instance);
        });

        Command<Messages.ShoppingCart.GetAllItems>(_ =>
        {
            Sender.Tell(_cart.Values.ToHashSet());
        });
        
        Command<Messages.ShoppingCart.GetTotalItemsInCart>(_ =>
        {
            Sender.Tell(_cart.Count);
        });
        
        CommandAsync<Messages.ShoppingCart.RemoveItem>(async message =>
        {
            var sender = Sender;
            var product = message.Product;
            if (!_cart.TryGetValue(product.Id, out var cartItem))
                return;
            
            await _productRegion.Ask<Done>(new Product.Return(product.Id, cartItem.Quantity));

            if (_cart.ContainsKey(product.Id))
            {
                _cart = _cart.Remove(product.Id);
                SaveSnapshot(_cart);
            }
            sender.Tell(Done.Instance);
        });
    }
    
    protected override bool Receive(object message)
    {
        if (message is SaveSnapshotSuccess)
            return true;
        
        return base.Receive(message);
    }

    private CartItem ToCartItem(int quantity, ProductDetails product) =>
        new(PersistenceId, quantity, product);
}