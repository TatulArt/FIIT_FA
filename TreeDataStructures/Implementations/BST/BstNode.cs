using TreeDataStructures.Core;

namespace TreeDataStructures.Implementations.BST;

public class BstNode<TKey, TValue>(TKey key, TValue value)
    : Node<TKey, TValue, BstNode<TKey, TValue>>(key, value)
{
}