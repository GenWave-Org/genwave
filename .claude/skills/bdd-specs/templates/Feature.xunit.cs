// STORY-000 — <story title goes here; this comment is the only place the id appears>
//
// BDD specification — xUnit
//
// Run with: dotnet test
//
// Structure (do not flatten):
//   Feature        -> top-level class, one per file, from a user story
//     Scenario     -> nested class; arranges ALL its data in the constructor
//                     (or an IClassFixture for expensive shared arrange)
//       Specification -> [Fact]; EXACTLY ONE assertion per fact
//
// Happy-path scenarios first and exhaustive.
// Sad-path scenarios in their OWN nested classes, below.
//
// Replace ShoppingCart and the domain calls with the real subject once
// these specs are approved. The subject does not need to exist yet — these
// specs are the design and are expected to fail first.

namespace GenWave.Example.Tests;

public static class FeatureACustomerBuildsAShoppingCart
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — exhaustive success outcomes, first.
    // ---------------------------------------------------------------------

    public sealed class ScenarioAddingTwoDistinctItems
    {
        private readonly ShoppingCart cart;

        // Arrange everything this scenario needs, once, here.
        public ScenarioAddingTwoDistinctItems()
        {
            cart = new ShoppingCart();
            cart.Add(new LineItem("widget", UnitPrice: 5m, Quantity: 2));
            cart.Add(new LineItem("gadget", UnitPrice: 10m, Quantity: 1));
        }

        [Fact]
        public void HoldsTwoLineItems() => Assert.Equal(2, cart.LineItems.Count);

        [Fact]
        public void SumsTheWidgetLine() => Assert.Equal(10m, cart.LineTotal("widget"));

        [Fact]
        public void SumsTheGadgetLine() => Assert.Equal(10m, cart.LineTotal("gadget"));

        [Fact]
        public void TotalsTheWholeCart() => Assert.Equal(20m, cart.Total);

        [Fact]
        public void IsNotEmpty() => Assert.False(cart.IsEmpty);
    }

    public sealed class ScenarioAddingTheSameSkuTwiceMergesQuantities
    {
        private readonly ShoppingCart cart;

        public ScenarioAddingTheSameSkuTwiceMergesQuantities()
        {
            cart = new ShoppingCart();
            cart.Add(new LineItem("widget", UnitPrice: 5m, Quantity: 1));
            cart.Add(new LineItem("widget", UnitPrice: 5m, Quantity: 3));
        }

        [Fact]
        public void KeepsASingleLineItem() => Assert.Single(cart.LineItems);

        [Fact]
        public void AccumulatesTheQuantity() => Assert.Equal(4, cart.QuantityOf("widget"));

        [Fact]
        public void PricesTheMergedQuantity() => Assert.Equal(20m, cart.Total);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated. Each failure mode is its own Scenario.
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingANonPositiveQuantity
    {
        private readonly Action act;

        public ScenarioRejectingANonPositiveQuantity()
        {
            var cart = new ShoppingCart();
            act = () => cart.Add(new LineItem("widget", UnitPrice: 5m, Quantity: 0));
        }

        [Fact]
        public void ThrowsACartException() => Assert.Throws<CartException>(act);
    }

    public sealed class ScenarioRejectingANegativeUnitPrice
    {
        private readonly Action act;

        public ScenarioRejectingANegativeUnitPrice()
        {
            var cart = new ShoppingCart();
            act = () => cart.Add(new LineItem("widget", UnitPrice: -1m, Quantity: 1));
        }

        [Fact]
        public void ThrowsACartException() => Assert.Throws<CartException>(act);
    }
}
