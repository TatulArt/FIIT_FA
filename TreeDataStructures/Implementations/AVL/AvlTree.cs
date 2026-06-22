using TreeDataStructures.Core;

namespace TreeDataStructures.Implementations.AVL;

public class AvlTree<TKey, TValue> : BinarySearchTreeBase<TKey, TValue, AvlNode<TKey, TValue>>
    where TKey : IComparable<TKey>
{
    protected override AvlNode<TKey, TValue> CreateNode(TKey key, TValue value)
        => new(key, value);
    
    protected override void OnNodeAdded(AvlNode<TKey, TValue> newNode)
    {
        ArgumentNullException.ThrowIfNull(newNode);
        
        AvlNode<TKey, TValue> ?node = newNode.Parent;
        
        while (node != null)
        {
            AvlNode<TKey, TValue> ?parent = node.Parent;
            AvlNode<TKey, TValue> root = Balance(node);

            if (root != node)
            {
                if (parent == null)
                {
                    Root = root;
                } else if (parent.Left == node)
                {
                    parent.Left = root;
                } else
                {
                    parent.Right = root;
                }
            }
            node = parent;
        }     
    } 

    private int UpdateHeight(AvlNode<TKey, TValue> node)
    {
        return Math.Max(GetHeight(node.Left), GetHeight(node.Right)) + 1;
    }

    private int GetHeight(AvlNode<TKey, TValue>? node)
    {
        return node?.Height ?? 0;
    }

    private int CountBalanceFactor(AvlNode<TKey, TValue> node)
    {
        return GetHeight(node.Right) - GetHeight(node.Left);
    }

    private int GetBalanceFactor(AvlNode<TKey, TValue> node)
    {
        return node == null ? 0 : CountBalanceFactor(node);
    }

    private AvlNode<TKey, TValue> Balance(AvlNode<TKey, TValue> node)
    {
        ArgumentNullException.ThrowIfNull(node);
        int balanceFactor = GetBalanceFactor(node);
        if (balanceFactor < -1) // перекос влево
        {
            if (GetBalanceFactor(node.Left) > 0)
            {
                node.Left = RotateLeftAvl(node.Left);
                return RotateRightAvl(node);
            } else
            {
                return RotateRightAvl(node);          
            }
        } else if (balanceFactor > 1) // перекос вправо
        {
            if (GetBalanceFactor(node.Right) < 0)
            {
                node.Right = RotateRightAvl(node.Right);
                return RotateLeftAvl(node);
            } else
            {
                return RotateLeftAvl(node);
            }
        }
        return node;
    }
    private AvlNode<TKey, TValue> RotateRightAvl(AvlNode<TKey, TValue> node)
    {
        AvlNode<TKey, TValue> left = node.Left ?? throw new InvalidOperationException("cannot rotate!");
        RotateRight(node);
        UpdateHeight(node);
        UpdateHeight(left);
        return left;
    }

    private AvlNode<TKey, TValue> RotateLeftAvl(AvlNode<TKey, TValue> node)
    {
        AvlNode<TKey, TValue> right = node.Right ?? throw new InvalidOperationException("cannot rotate!");
        RotateLeft(node);
        UpdateHeight(node);
        UpdateHeight(right);
        return right;
    }
}
