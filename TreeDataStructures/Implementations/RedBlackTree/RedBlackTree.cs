using TreeDataStructures.Core;

namespace TreeDataStructures.Implementations.RedBlackTree;

public class RedBlackTree<TKey, TValue> : BinarySearchTreeBase<TKey, TValue, RbNode<TKey, TValue>>
{
    protected override RbNode<TKey, TValue> CreateNode(TKey key, TValue value)
    {
        return new(key, value);
    }
    
    protected override void OnNodeAdded(RbNode<TKey, TValue>? newNode)
    {
        if (newNode == Root)
        {
            SetColor(newNode, RbColor.Black);
            return;
        }
        
        while (GetColor(newNode!.Parent) == RbColor.Red)
        {
            RbNode<TKey, TValue>? grandParent = Grandparent(newNode);
            RbNode<TKey, TValue>? uncle = Uncle(newNode);
            RbNode<TKey, TValue>? parent = newNode.Parent;

            if (GetColor(uncle) == RbColor.Red) // Случай 1
            {
                SetColor(parent, RbColor.Black);
                SetColor(uncle, RbColor.Black);
                SetColor(grandParent, RbColor.Red);
                newNode = grandParent;
            }
            else
            {
                if (parent!.IsLeftChild)
                {
                    if (newNode.IsRightChild) // случай 2
                    {
                        newNode = parent;
                        RotateLeft(newNode);
                        parent = newNode.Parent;
                        grandParent = Grandparent(newNode);
                    }

                    // случай 3
                    SetColor(parent, RbColor.Black);
                    SetColor(grandParent, RbColor.Red);
                    RotateRight(grandParent!);
                }
                else
                {
                    if (newNode.IsLeftChild) // случай 2
                    {
                        newNode = parent;
                        RotateRight(newNode);
                        parent = newNode.Parent;
                        grandParent = Grandparent(newNode);
                    }
                    
                    // случай 3
                    SetColor(parent, RbColor.Black);
                    SetColor(grandParent, RbColor.Red);
                    RotateLeft(grandParent!);
                }
            }
        }
        
        SetColor(Root, RbColor.Black);
    }
    
    protected override void OnNodeRemoved(RbNode<TKey, TValue>? parent, RbNode<TKey, TValue>? child)
    {
        while (child != Root && GetColor(child) == RbColor.Black)
        {
            bool isLeft;
            if (child != null)
            {
                isLeft = child.IsLeftChild;
            }
            else if (parent != null)
            {
                isLeft = parent.Left == null; // удалённый узел был слева
            }
            else
            {
                break;
            }

            if (parent == null)
            {
                break;
            }

            if (isLeft)
            {
                var sibling = parent.Right;

                if (GetColor(sibling) == RbColor.Red) // Случай 1
                {
                    SetColor(sibling, RbColor.Black);
                    SetColor(parent, RbColor.Red);
                    RotateLeft(parent);
                    sibling = parent.Right;
                }

                if (GetColor(sibling?.Left) == RbColor.Black && GetColor(sibling?.Right) == RbColor.Black) // Случай 2
                {
                    SetColor(sibling, RbColor.Red);
                    child = parent;
                    parent = child?.Parent;
                    continue;
                }

                if (GetColor(sibling?.Right) == RbColor.Black) // Случай 3
                {
                    SetColor(sibling?.Left, RbColor.Black);
                    SetColor(sibling, RbColor.Red);
                    RotateRight(sibling!);
                    sibling = parent.Right;
                }

                // Случай 4
                SetColor(sibling, GetColor(parent));
                SetColor(parent, RbColor.Black);
                SetColor(sibling?.Right, RbColor.Black);
                RotateLeft(parent);
                child = Root;
                parent = null;
            }
            else
            {
                var sibling = parent.Left;

                if (GetColor(sibling) == RbColor.Red) // Случай 1
                {
                    SetColor(sibling, RbColor.Black);
                    SetColor(parent, RbColor.Red);
                    RotateRight(parent);
                    sibling = parent.Left;
                }

                if (GetColor(sibling?.Left) == RbColor.Black && GetColor(sibling?.Right) == RbColor.Black) // Случай 2
                {
                    SetColor(sibling, RbColor.Red);
                    child = parent;
                    parent = child?.Parent;
                    continue;
                }

                if (GetColor(sibling?.Left) == RbColor.Black) // Случай 3
                {
                    SetColor(sibling?.Right, RbColor.Black);
                    SetColor(sibling, RbColor.Red);
                    RotateLeft(sibling!);
                    sibling = parent.Left;
                }

                // Случай 4
                SetColor(sibling, GetColor(parent));
                SetColor(parent, RbColor.Black);
                SetColor(sibling?.Left, RbColor.Black);
                RotateRight(parent);
                child = Root;
                parent = null;
            }
        }

        SetColor(child, RbColor.Black);
        SetColor(Root, RbColor.Black);
    }

    private static RbNode<TKey, TValue>? Sibling(RbNode<TKey, TValue>? node)
    {
        if (node?.Parent == null) return null;
        
        return node.IsLeftChild ? node.Parent.Right : node.Parent.Left;
    }

    private static RbNode<TKey, TValue>? Grandparent(RbNode<TKey, TValue>? node)
    {
        return node?.Parent?.Parent;
    }

    private static RbNode<TKey, TValue>? Uncle(RbNode<TKey, TValue>? node)
    {
        RbNode<TKey, TValue>? grand = Grandparent(node);
        if (grand == null) return null;
        return node!.Parent!.IsLeftChild ? grand.Left : grand.Right;
    }

    private static RbColor GetColor(RbNode<TKey, TValue> ?node)
    {
        return node?.Color ?? RbColor.Black;
    }
    
    private static void SetColor(RbNode<TKey, TValue> ?node, RbColor color)
    {
        node?.Color = color;
    }
}