using FixTrack.Domain.Common;

namespace FixTrack.Tests.Domain;

public sealed class EntityTests
{
    [Fact]
    public void Entities_of_the_same_type_and_id_are_equal()
    {
        var first = new Customer(id: 7);
        var second = new Customer(id: 7);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Entities_with_different_ids_are_not_equal()
    {
        Assert.NotEqual(new Customer(id: 1), new Customer(id: 2));
    }

    [Fact]
    public void Entities_of_different_types_are_not_equal_even_with_the_same_id()
    {
        Entity customer = new Customer(id: 3);
        Entity device = new Device(id: 3);

        Assert.False(customer.Equals(device));
        Assert.True(customer != device);
    }

    [Fact]
    public void Transient_entities_are_only_equal_to_themselves()
    {
        var first = new Customer();
        var second = new Customer();

        Assert.True(first.IsTransient);
        Assert.Equal(first, first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void An_entity_is_never_equal_to_null()
    {
        var customer = new Customer(id: 1);

        Assert.False(customer.Equals(null));
        Assert.False(customer == null);
    }

    private sealed class Customer : Entity
    {
        public Customer()
        {
        }

        public Customer(int id) => Id = id;
    }

    private sealed class Device : Entity
    {
        public Device(int id) => Id = id;
    }
}
