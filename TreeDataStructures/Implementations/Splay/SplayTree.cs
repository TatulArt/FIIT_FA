using System.Diagnostics.CodeAnalysis;
using TreeDataStructures.Implementations.BST;

namespace TreeDataStructures.Implementations.Splay;

public class SplayTree<TKey, TValue> : BinarySearchTree<TKey, TValue>
    where TKey : IComparable<TKey>
{
    protected override BstNode<TKey, TValue> CreateNode(TKey key, TValue value)
        => new(key, value);
    
    protected override void OnNodeAdded(BstNode<TKey, TValue> newNode)
    {
        Splay(newNode);
    }
    
    protected override void OnNodeRemoved(BstNode<TKey, TValue>? parent, BstNode<TKey, TValue>? child)
    {
        if (parent != null)
            Splay(parent);
    }
    
    public override bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var node = FindNode(key);
        
        if (node != null)
        {
            value = node.Value;
            Splay(node);          
            return true;
        }
        
        value = default;
        return false;       
    }

    private void Splay(BstNode<TKey, TValue> node)
    {
        while (node.Parent != null)
        {
            BstNode<TKey, TValue> parent = node.Parent;
            BstNode<TKey, TValue> ?grandparent = node.Parent.Parent;

            if (grandparent == null)
            {
                if (node.IsLeftChild)
                {
                    RotateRight(parent);
                } else
                {
                    RotateLeft(parent);
                }
            } else if (node.IsLeftChild)
            {
                if (parent.IsRightChild)
                {
                    RotateRight(parent);
                    RotateLeft(grandparent);
                } else
                {
                    RotateDoubleRight(parent);
                }
            } else if (node.IsRightChild)
            {
                if (parent.IsRightChild)
                {
                    RotateDoubleLeft(parent);
                } else
                {
                    RotateLeft(parent);
                    RotateRight(grandparent);
                }
            } 
        }
    }

    public override bool ContainsKey(TKey key)
    {
        var node = FindNode(key); 
        if (node != null)
        {
            Splay(node);          
            return true;
        }
        return false;    
    }
}