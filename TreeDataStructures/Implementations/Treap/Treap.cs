using TreeDataStructures.Core;

namespace TreeDataStructures.Implementations.Treap;

public class Treap<TKey, TValue> : BinarySearchTreeBase<TKey, TValue, TreapNode<TKey, TValue>>
    where TKey : IComparable<TKey>
{
    /// <summary>
    /// Разрезает дерево с корнем <paramref name="root"/> на два поддерева:
    /// Left: все ключи <= <paramref name="key"/>
    /// Right: все ключи > <paramref name="key"/>
    /// </summary>
    protected virtual (TreapNode<TKey, TValue>? Left, TreapNode<TKey, TValue>? Right) Split(TreapNode<TKey, TValue>? root, TKey key)
    {
        if (root == null)
            return (null, null);

        if (Comparer.Compare(key, root.Key) < 0) {
            var (left, right) = Split(root.Left, key);
            root.Left = right;
            UpdateParent(root, root.Left);
            return (left, root);
        } else {
            var (left, right) = Split(root.Right, key);
            root.Right = left;
            UpdateParent(root, root.Right);
            return (root, right);
        }
    }

    /// <summary>
    /// Сливает два дерева в одно.
    /// Важное условие: все ключи в <paramref name="left"/> должны быть меньше ключей в <paramref name="right"/>.
    /// Слияние происходит на основе Priority (куча).
    /// </summary>
    protected virtual TreapNode<TKey, TValue>? Merge(TreapNode<TKey, TValue>? left, TreapNode<TKey, TValue>? right)
    {
        if (right == null)
        {
            return left;
        }
        if (left == null)
        {
            return right;
        } else if (left.Priority > right.Priority)
        {
            left.Right = Merge(left.Right, right);
            UpdateParent(left, left.Right);
            return left;
        } else
        {
            right.Left = Merge(left, right.Left);
            UpdateParent(right, right.Left);            
            return right;
        }
    }
    

    public override void Add(TKey key, TValue value)
    {
        var existing = FindNode(key);
        if (existing != null)
        {
            existing.Value = value;
            return;
        }
        var node = CreateNode(key, value);
        var (left, right) = Split(Root, key);
        Root = Merge(Merge(left, node), right);
        Count++;
    }

    public override bool Remove(TKey key)
    {
        var node = FindNode(key);
        if (node == null)
        {
            return false;
        }
        var parent = node.Parent;
        bool isLeft = node.IsLeftChild;
        var newNode = Merge(node.Left, node.Right);
        UpdateParent(parent, newNode);
        
        if (parent == null)
        {
            Root = newNode;
        } else
        {
            if (isLeft)
            {
                parent.Left = newNode;
            } else
            {
                parent.Right = newNode;
            }
        }

        this.Count--;
        return true;
    }

    protected override TreapNode<TKey, TValue> CreateNode(TKey key, TValue value)
    {
        return new(key, value);
    }

    private void UpdateParent(TreapNode<TKey, TValue> ?parent, TreapNode<TKey, TValue> ?child)
    {
        if (child != null)
        {
            child.Parent = parent;
        }
    }

    protected override void OnNodeAdded(TreapNode<TKey, TValue> newNode)
    {
    }
    
    protected override void OnNodeRemoved(TreapNode<TKey, TValue>? parent, TreapNode<TKey, TValue>? child)
    {
    }
    
}