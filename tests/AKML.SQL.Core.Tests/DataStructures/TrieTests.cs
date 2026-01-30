using AKML.SQL.Core.DataStructures;
using FluentAssertions;
using Xunit;

namespace AKML.SQL.Core.Tests.DataStructures
{
    /// <summary>
    /// Tests for Trie data structure.
    /// Covers TC-4.2.10: Trie Data Structure Integrity.
    /// </summary>
    public class TrieTests
    {
        [Fact]
        public void Add_SingleItem_CanBeRetrieved()
        {
            // Arrange
            var trie = new Trie<string>();

            // Act
            trie.Add("Customers", "Table:Customers");

            // Assert
            trie.Count.Should().Be(1);
            trie.Contains("Customers").Should().BeTrue();
            trie.TryGetValue("Customers", out var value).Should().BeTrue();
            value.Should().Be("Table:Customers");
        }

        [Fact]
        public void Add_MultipleItems_AllCanBeRetrieved()
        {
            // Arrange
            var trie = new Trie<string>();
            var items = new[] { "Customers", "CustomerOrders", "Products", "ProductCategories", "Orders" };

            // Act
            foreach (var item in items)
            {
                trie.Add(item, $"Table:{item}");
            }

            // Assert
            trie.Count.Should().Be(5);
            foreach (var item in items)
            {
                trie.Contains(item).Should().BeTrue($"Trie should contain {item}");
            }
        }

        [Fact]
        public void Add_CaseInsensitive_MatchesRegardlessOfCase()
        {
            // Arrange
            var trie = new Trie<string>(caseSensitive: false);

            // Act
            trie.Add("Customers", "Table:Customers");

            // Assert
            trie.Contains("customers").Should().BeTrue();
            trie.Contains("CUSTOMERS").Should().BeTrue();
            trie.Contains("CuStOmErS").Should().BeTrue();
        }

        [Fact]
        public void Remove_ExistingItem_RemovesSuccessfully()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");
            trie.Add("Products", "Table:Products");

            // Act
            var removed = trie.Remove("Customers");

            // Assert
            removed.Should().BeTrue();
            trie.Count.Should().Be(1);
            trie.Contains("Customers").Should().BeFalse();
            trie.Contains("Products").Should().BeTrue();
        }

        [Fact]
        public void Remove_NonExistingItem_ReturnsFalse()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");

            // Act
            var removed = trie.Remove("NonExistent");

            // Assert
            removed.Should().BeFalse();
            trie.Count.Should().Be(1);
        }

        [Fact]
        public void GetByPrefix_ReturnsMatchingItems()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");
            trie.Add("CustomerOrders", "Table:CustomerOrders");
            trie.Add("CustomerAddresses", "Table:CustomerAddresses");
            trie.Add("Products", "Table:Products");

            // Act
            var results = trie.GetByPrefix("Cust").ToList();

            // Assert
            results.Should().HaveCount(3);
            results.Select(r => r.Key).Should().Contain(new[] { "Customers", "CustomerOrders", "CustomerAddresses" });
        }

        [Fact]
        public void GetByPrefix_EmptyPrefix_ReturnsAllItems()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");
            trie.Add("Products", "Table:Products");
            trie.Add("Orders", "Table:Orders");

            // Act
            var results = trie.GetByPrefix("").ToList();

            // Assert
            results.Should().HaveCount(3);
        }

        [Fact]
        public void GetByPrefix_NoMatches_ReturnsEmpty()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");
            trie.Add("Products", "Table:Products");

            // Act
            var results = trie.GetByPrefix("XYZ").ToList();

            // Assert
            results.Should().BeEmpty();
        }

        [Fact]
        public void GetByPrefix_RespectsMaxResults()
        {
            // Arrange
            var trie = new Trie<string>();
            for (var i = 0; i < 100; i++)
            {
                trie.Add($"Item{i:D3}", $"Value{i}");
            }

            // Act
            var results = trie.GetByPrefix("Item", maxResults: 10).ToList();

            // Assert
            results.Should().HaveCount(10);
        }

        [Fact]
        public void FuzzySearch_ExactMatch_ReturnsHighScore()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");
            trie.Add("CustomerOrders", "Table:CustomerOrders");

            // Act
            var results = trie.FuzzySearch("Customers").ToList();

            // Assert
            results.Should().NotBeEmpty();
            results.First().Key.Should().Be("Customers");
            results.First().Score.Should().Be(100);
        }

        [Fact]
        public void FuzzySearch_PrefixMatch_ReturnsHighScore()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");
            trie.Add("Products", "Table:Products");

            // Act
            var results = trie.FuzzySearch("Cust").ToList();

            // Assert
            results.Should().NotBeEmpty();
            results.First().Key.Should().Be("Customers");
            results.First().Score.Should().BeGreaterThan(70);
        }

        [Fact]
        public void FuzzySearch_SubstringMatch_ReturnsMediumScore()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("MyCustomers", "Table:MyCustomers");

            // Act
            var results = trie.FuzzySearch("Custom").ToList();

            // Assert
            results.Should().NotBeEmpty();
            results.First().Key.Should().Be("MyCustomers");
            results.First().Score.Should().BeInRange(50, 80);
        }

        [Fact]
        public void FuzzySearch_WordBoundaryMatch_ReturnsResults()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("CustomerOrder", "Table:CustomerOrder");
            trie.Add("ProductCategory", "Table:ProductCategory");

            // Act
            var results = trie.FuzzySearch("CO").ToList();

            // Assert
            results.Should().NotBeEmpty();
            results.Any(r => r.Key == "CustomerOrder").Should().BeTrue();
        }

        [Fact]
        public void FuzzySearch_SubsequenceMatch_ReturnsResults()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("CustomerOrder", "Table:CustomerOrder");

            // Act
            var results = trie.FuzzySearch("cuord", maxResults: 50, minScore: 20).ToList();

            // Assert
            results.Should().NotBeEmpty();
            results.First().Key.Should().Be("CustomerOrder");
        }

        [Fact]
        public void FuzzySearch_NoMatch_ReturnsEmpty()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Customers", "Table:Customers");

            // Act
            var results = trie.FuzzySearch("XYZ", minScore: 50).ToList();

            // Assert
            results.Should().BeEmpty();
        }

        [Fact]
        public void Clear_RemovesAllItems()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("Item1", "Value1");
            trie.Add("Item2", "Value2");
            trie.Add("Item3", "Value3");

            // Act
            trie.Clear();

            // Assert
            trie.Count.Should().Be(0);
            trie.Contains("Item1").Should().BeFalse();
            trie.GetAllValues().Should().BeEmpty();
        }

        [Fact]
        public void Performance_Add10KItems_CompletesQuickly()
        {
            // Arrange
            var trie = new Trie<string>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            for (var i = 0; i < 10000; i++)
            {
                trie.Add($"Table_{i:D5}", $"Value_{i}");
            }
            stopwatch.Stop();

            // Assert
            trie.Count.Should().Be(10000);
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, "Adding 10K items should complete in under 1 second");
        }

        [Fact]
        public void Performance_PrefixSearch10KItems_CompletesUnder10ms()
        {
            // Arrange
            var trie = new Trie<string>();
            for (var i = 0; i < 10000; i++)
            {
                trie.Add($"Table_{i:D5}", $"Value_{i}");
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act - Run 100 searches
            for (var i = 0; i < 100; i++)
            {
                var results = trie.GetByPrefix("Table_001", maxResults: 50).ToList();
            }
            stopwatch.Stop();

            // Assert
            var avgTime = stopwatch.ElapsedMilliseconds / 100.0;
            avgTime.Should().BeLessThan(10, "Average prefix search should complete in under 10ms");
        }

        [Fact]
        public void GetAllValues_ReturnsAllItems()
        {
            // Arrange
            var trie = new Trie<string>();
            trie.Add("A", "1");
            trie.Add("B", "2");
            trie.Add("C", "3");

            // Act
            var results = trie.GetAllValues().ToList();

            // Assert
            results.Should().HaveCount(3);
            results.Select(r => r.Value).Should().Contain(new[] { "1", "2", "3" });
        }

        [Fact]
        public void OriginalKey_PreservedAfterCaseInsensitiveAdd()
        {
            // Arrange
            var trie = new Trie<string>(caseSensitive: false);

            // Act
            trie.Add("CustomerOrders", "Table:CustomerOrders");

            // Assert
            var results = trie.GetByPrefix("cust").ToList();
            results.Should().HaveCount(1);
            results.First().Key.Should().Be("CustomerOrders"); // Original case preserved
        }
    }
}
