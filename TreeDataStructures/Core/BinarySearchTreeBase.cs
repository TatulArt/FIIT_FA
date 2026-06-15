using System.Collections;
using System.Diagnostics.CodeAnalysis;
using TreeDataStructures.Interfaces;

namespace TreeDataStructures.Core;

public abstract class BinarySearchTreeBase<TKey, TValue, TNode>(IComparer<TKey>? comparer = null) 
    : ITree<TKey, TValue>
    where TNode : Node<TKey, TValue, TNode>
{
    protected TNode? Root;
    public IComparer<TKey> Comparer { get; protected set; } = comparer ?? Comparer<TKey>.Default; // use it to compare Keys

    public int Count { get; protected set; }
    
    public bool IsReadOnly => false;

    public ICollection<TKey> Keys => InOrder()
        .Select(e => e.Key)
        .ToList();

    public ICollection<TValue> Values => InOrder()
        .Select(e => e.Value)
        .ToList();
    
    public virtual void Add(TKey key, TValue value)
    {
        var newNode = CreateNode(key, value);
        if (Root is null)
        {
            Root = newNode;
            Count++;
            OnNodeAdded(newNode);
            return;
        }
        var current = Root;
        while (true)
        {
            var cmp = Comparer.Compare(key, current.Key);
            if (cmp == 0)
            {
                current.Value = newNode.Value;
                return;
            }
            if (cmp > 0)
            {
                if (current.Right is null)
                {
                    current.Right = newNode;
                    newNode.Parent = current;
                    break;
                }
                current = current.Right;
            }
            else
            {
                if (current.Left is null)
                {
                    current.Left = newNode;
                    newNode.Parent = current;
                    break;
                }
                current = current.Left;
            }
        }
        Count++;
        OnNodeAdded(newNode);
    }
    
    public virtual bool Remove(TKey key)
    {
        TNode? node = FindNode(key);
        if (node == null) { return false; }

        RemoveNode(node);
        this.Count--;
        return true;
    }
    
    protected virtual void RemoveNode(TNode node)
    {
        TNode? parent;
        TNode? child;
        if (node.Left is null)
        {
            parent = node.Parent;
            child = node.Right;
            Transplant(node, node.Right);
        }
        else if (node.Right is null)
        {
            parent = node.Parent;
            child = node.Left;
            Transplant(node, node.Left);
        }
        else
        {
            TNode candidate = node.Right;
            while (candidate.Left is not null) candidate = candidate.Left;
            if (candidate.Parent != node)
            {
                Transplant(candidate, candidate.Right);
                candidate.Right = node.Right;
                candidate.Right.Parent = candidate;
            }
            Transplant(node, candidate);
            candidate.Left = node.Left;
            candidate.Left.Parent = candidate;
            parent = candidate;
            child = candidate.Right;
        }
        OnNodeRemoved(parent, child);
    }

    public virtual bool ContainsKey(TKey key) => FindNode(key) != null;
    
    public virtual bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        TNode? node = FindNode(key);
        if (node != null)
        {
            value = node.Value;
            return true;
        }
        value = default;
        return false;
    }

    public TValue this[TKey key]
    {
        get => TryGetValue(key, out TValue? val) ? val : throw new KeyNotFoundException();
        set => Add(key, value);
    }

    
    #region Hooks
    
    /// <summary>
    /// Вызывается после успешной вставки
    /// </summary>
    /// <param name="newNode">Узел, который встал на место</param>
    protected virtual void OnNodeAdded(TNode newNode) { }
    
    /// <summary>
    /// Вызывается после удаления. 
    /// </summary>
    /// <param name="parent">Узел, чей ребенок изменился</param>
    /// <param name="child">Узел, который встал на место удаленного</param>
    protected virtual void OnNodeRemoved(TNode? parent, TNode? child) { }
    
    #endregion
    
    
    #region Helpers
    protected abstract TNode CreateNode(TKey key, TValue value);
    
    
    protected TNode? FindNode(TKey key)
    {
        TNode? current = Root;
        while (current != null)
        {
            int cmp = Comparer.Compare(key, current.Key);
            if (cmp == 0) { return current; }
            current = cmp < 0 ? current.Left : current.Right;
        }
        return null;
    }

    protected void RotateLeft(TNode x)
    {
        if (x.Right is null) return;
        var y = x.Right;

        x.Right = y.Left;
        x.Right?.Parent = x;

        Transplant(x, y);

        y.Left = x;
        x.Parent = y;
    }

    protected void RotateRight(TNode y)
    {
        if (y.Left is null) return;
        var x = y.Left;

        y.Left = x.Right;
        x.Right?.Parent = y;

        Transplant(y, x);

        x.Right = y;
        y.Parent = x;
    }

    protected void RotateBigLeft(TNode x)
    {
        if (x.Right is null) return;
        RotateRight(x.Right);
        RotateLeft(x);
    }
    
    protected void RotateBigRight(TNode y)
    {
        if (y.Left is null) return;
        RotateLeft(y.Left);
        RotateRight(y);
    }
    
    protected void RotateDoubleLeft(TNode gparent)
    {
        var parent = gparent.Right;
        if (parent is null)
            return;
        RotateLeft(gparent);
        RotateLeft(parent);
    }
    
    protected void RotateDoubleRight(TNode gparent)
    {
        var parent = gparent.Left;
        if (parent is null)
            return;
        RotateRight(gparent);
        RotateRight(parent);
    }

    
    protected void Transplant(TNode u, TNode? v)
    {
        if (u.Parent == null)
        {
            Root = v;
        }
        else if (u.IsLeftChild)
        {
            u.Parent.Left = v;
        }
        else
        {
            u.Parent.Right = v;
        }
        v?.Parent = u.Parent;
    }
    #endregion
    
    public IEnumerable<TreeEntry<TKey, TValue>>  InOrder() => new TreeIterator(Root, TraversalStrategy.InOrder);
    public IEnumerable<TreeEntry<TKey, TValue>>  PreOrder() => new TreeIterator(Root, TraversalStrategy.PreOrder);
    public IEnumerable<TreeEntry<TKey, TValue>>  PostOrder() => new TreeIterator(Root, TraversalStrategy.PostOrder);
    public IEnumerable<TreeEntry<TKey, TValue>>  InOrderReverse() => new TreeIterator(Root, TraversalStrategy.InOrderReverse);
    public IEnumerable<TreeEntry<TKey, TValue>>  PreOrderReverse() => new TreeIterator(Root, TraversalStrategy.PreOrderReverse);
    public IEnumerable<TreeEntry<TKey, TValue>>  PostOrderReverse() => new TreeIterator(Root, TraversalStrategy.PostOrderReverse);
    
    /// <summary>
    /// Внутренний класс-итератор. 
    /// Реализует паттерн Iterator вручную, без yield return (ban).
    /// </summary>
    private struct TreeIterator : // Подписываемся сразу на оба интерфейса, чтобы можно было получать и Current, и двигать указатель
        IEnumerable<TreeEntry<TKey, TValue>>, // в зависимости от стратегии
        IEnumerator<TreeEntry<TKey, TValue>>
    {
        // probably add something here
        private readonly TraversalStrategy _strategy; // or make it template parameter?
        private readonly TNode? Root;
        private TNode? currentAlgo;
        private TNode? current;
        private TNode? previous = null;
        public IEnumerator<TreeEntry<TKey, TValue>> GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
        
         public TreeEntry<TKey, TValue> Current  
        {
            get
            {
                if (current == null) {
                    throw new InvalidOperationException("Enumeration not started or has ended");
                }
                
                int depth = 0;
                TNode ?cur = current;
                
                while (cur.Parent != null)
                {
                    depth++;
                    cur = cur.Parent; 
                }
                
                return new TreeEntry<TKey, TValue>(current.Key, current.Value, depth);    
            }

        }
        object IEnumerator.Current => Current;
        
        public TreeIterator(TNode? root, TraversalStrategy strategy)
        {
            if (root == null)
            {
                Console.WriteLine("null root");
            }
            this._strategy = strategy;
            this.Root = root;
            this.current = root;
            this.previous = null;
            this.currentAlgo = root;
        }
        
        public bool MoveNext()
        {
            if (current == null)
            {
                return false;
            } 
            else
            {
                switch (_strategy)
                {
                    case TraversalStrategy.InOrder:
                        return MoveNextInOrder();
                    case TraversalStrategy.PreOrder:
                        return MoveNextPreOrder();
                    case TraversalStrategy.PostOrder:
                        return MoveNextPostOrder();
                    case TraversalStrategy.InOrderReverse:
                        return MoveNextInOrderReverse();
                    case TraversalStrategy.PreOrderReverse:
                        return MoveNextPreOrderReverse();
                    case TraversalStrategy.PostOrderReverse:
                        return MoveNextPostOrderReverse();
                    default:
                        throw new InvalidOperationException("Do not have such traversal!\n");
                }
            }
        }

        private bool MoveNextInOrder() // лево - корень - право
        {
            while (currentAlgo != null)
            {
                if (previous == currentAlgo.Parent) // спускаемся
                {
                    previous = currentAlgo;
                    if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left; // идем налево до упора
                    } else
                    {
                        current = currentAlgo; // уже нашли самый левый, следующим в итераторе будет он
                        if (currentAlgo.Right != null) 
                        {
                            currentAlgo = currentAlgo.Right; // проверяем правое поддерево крайнего левого узла
                        } else
                        {
                            currentAlgo = currentAlgo.Parent; // если его нет, начинам подниматься
                        }
                        return true;
                    }
                } else if (previous == currentAlgo.Left) // поднимаемся, пришли из левого поддерева
                {
                    previous = currentAlgo;
                    current = currentAlgo; // следующим будет этот узел
                    if (currentAlgo.Right != null) // если у него есть правое поддерево, идем по нему
                    {
                        currentAlgo = currentAlgo.Right;
                    } else 
                    {
                        currentAlgo = currentAlgo.Parent; // если нет, идем выше
                    }
                    return true;
                } else if (previous == currentAlgo.Right) // поднимаемся, пришли из правого поддерева
                {
                    previous = currentAlgo; // если это произошло, значит, что левое поддерево, сам узел и правое уже выдали итератору
                    currentAlgo = currentAlgo.Parent; // следовательно, просто поднимаемся выше
                }                
            }
            return false; // анлак
        }

        private bool MoveNextPreOrder()
        {
            while (currentAlgo != null)
            {
                if (previous == currentAlgo.Parent)
                {
                    previous = currentAlgo;
                    current = currentAlgo;
                    if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left;
                    } else if (currentAlgo.Right != null)
                    {
                        currentAlgo = currentAlgo.Right;
                    } else {
                        currentAlgo = currentAlgo.Parent;
                    }
                    return true;
                } else if (previous == currentAlgo.Left)
                {
                    previous = currentAlgo;
                    if (currentAlgo.Right != null)
                    {
                        currentAlgo = currentAlgo.Right;

                    } else
                    {
                        currentAlgo = currentAlgo.Parent;
                    }
                } else if (previous == currentAlgo.Right)
                {
                    previous = currentAlgo;
                    currentAlgo = currentAlgo.Parent;
                }                
            }
            return false;
        }

        private bool MoveNextPostOrder()
        {
            while (currentAlgo != null)
            {
                if (previous == currentAlgo.Parent)
                {
                    previous = currentAlgo;
                    if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left;
                    } else
                    {
                        current = currentAlgo;
                        if (currentAlgo.Right != null)
                        {
                            currentAlgo = currentAlgo.Right;
                        } else
                        {
                            currentAlgo = currentAlgo.Parent;
                        }
                        return true;
                    }
                } else if (previous == currentAlgo.Left)
                {
                    previous = currentAlgo;
                    if (currentAlgo.Right != null)
                    {
                        currentAlgo = currentAlgo.Right;

                    } else
                    {
                        currentAlgo = currentAlgo.Parent;
                    }
                } else if (previous == currentAlgo.Right)
                {
                    current = currentAlgo;
                    previous = currentAlgo;
                    currentAlgo = currentAlgo.Parent;
                    return true;
                }                
            }
            return false;
        }

        private bool MoveNextInOrderReverse()
        {
            while (currentAlgo != null)
            {
                if (previous == currentAlgo.Parent)
                {
                    previous = currentAlgo;
                    if (currentAlgo.Right != null)
                    {
                        currentAlgo = currentAlgo.Right;
                    } else
                    {
                        current = currentAlgo;
                        if (currentAlgo.Left != null)
                        {
                            currentAlgo = currentAlgo.Left;
                        } else
                        {
                            currentAlgo = currentAlgo.Parent;
                        }
                        return true;
                    } 
                } else if (previous == currentAlgo.Right)
                {
                    previous = currentAlgo;
                    current = currentAlgo;
                    if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left;
                    } else
                    {
                        currentAlgo = currentAlgo.Parent;
                    }
                    return true;
                } else if (previous == currentAlgo.Left)
                {
                    previous = currentAlgo;
                    currentAlgo = currentAlgo.Parent;
                }
            }
            return false;
        }

        private bool MoveNextPreOrderReverse()
        {
            while (currentAlgo != null)
            {
                if (previous == currentAlgo.Parent)
                {
                    previous = currentAlgo;
                    current = currentAlgo;
                    if (currentAlgo.Right != null)
                    {
                        currentAlgo = currentAlgo.Right;
                    } else if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left;
                    }
                    else
                    {
                        currentAlgo = currentAlgo.Parent;
                    }
                    return true;
                }
                else if (previous == currentAlgo.Right)
                {
                    previous = currentAlgo;
                    if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left;
                    }
                    else
                    {
                        currentAlgo = currentAlgo.Parent;
                    }
                }
                else if (previous == currentAlgo.Left)
                {
                    previous = currentAlgo;
                    currentAlgo = currentAlgo.Parent;
                }
            }
            return false;
        }
        private bool MoveNextPostOrderReverse()
        {
            while (currentAlgo != null)
            {
                if (previous == currentAlgo.Parent)
                {
                    previous = currentAlgo;
                    if (currentAlgo.Right != null)
                    {
                        currentAlgo = currentAlgo.Right;
                    }
                    else if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left;
                    }
                    else
                    {
                        current = currentAlgo;
                        currentAlgo = currentAlgo.Parent;
                        return true;
                    }
                }
                else if (previous == currentAlgo.Right)
                {
                    previous = currentAlgo;
                    if (currentAlgo.Left != null)
                    {
                        currentAlgo = currentAlgo.Left;
                    }
                    else
                    {
                        current = currentAlgo;
                        currentAlgo = currentAlgo.Parent;
                        return true;
                    }
                }
                else if (previous == currentAlgo.Left)
                {
                    current = currentAlgo;
                    previous = currentAlgo;
                    currentAlgo = currentAlgo.Parent;
                    return true;
                }
            }
            return false;
        }
        
        public void Reset()
        {
            current = Root;
            currentAlgo = Root;
            previous = null;
        }

        
        public void Dispose()
        {
            Reset();
        }
    }
    
    
    private enum TraversalStrategy { InOrder, PreOrder, PostOrder, InOrderReverse, PreOrderReverse, PostOrderReverse }
    
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return new TreeKeyValueEnumerator(Root);
    }
    
    private struct TreeKeyValueEnumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private TreeIterator iterator;
        private KeyValuePair<TKey, TValue> current;

        public TreeKeyValueEnumerator(TNode? root)
        {
            iterator = new TreeIterator(root, TraversalStrategy.InOrder);
            current = default;
        }

        public KeyValuePair<TKey, TValue> Current => current;
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (iterator.MoveNext())
            {
                var entry = iterator.Current;
                current = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                return true;
            }
            
            current = default;
            return false;
        }

        public void Reset()
        {
            iterator.Reset();
            current = default;
        }

        public void Dispose()
        {
            iterator.Dispose();
        }
    }

    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    public void Clear() { Root = null; Count = 0; }
    public bool Contains(KeyValuePair<TKey, TValue> item) => ContainsKey(item.Key);
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex >= array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Destination array is not long enough");

        foreach (var pair in this)
        {
            array[arrayIndex++] = pair;
        }
    }        
    public bool Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);
}