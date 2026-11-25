using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Prometheus.Common.Docking;

public class LayoutNode
{
    private double _ratio = 0.5;

    public LayoutNodeType Type { get; set; } = LayoutNodeType.Leaf;

    public SplitOrientation Orientation { get; set; } = SplitOrientation.Horizontal;

    /// <summary>
    /// Relative size ratio between FirstChild and SecondChild, strictly clamped between 0.10 and 0.90.
    /// </summary>
    public double Ratio
    {
        get => _ratio;
        set => _ratio = Math.Clamp(value, 0.10, 0.90);
    }

    /// <summary>
    /// Identifier of the panel if this node is a Leaf.
    /// </summary>
    public string? PanelId { get; set; }

    public LayoutNode? FirstChild { get; set; }

    public LayoutNode? SecondChild { get; set; }

    public LayoutNode()
    {
    }

    public static LayoutNode CreateLeaf(string panelId)
    {
        return new LayoutNode
        {
            Type = LayoutNodeType.Leaf,
            PanelId = panelId
        };
    }

    public static LayoutNode CreateSplit(
        SplitOrientation orientation,
        LayoutNode firstChild,
        LayoutNode secondChild,
        double ratio = 0.5)
    {
        return new LayoutNode
        {
            Type = LayoutNodeType.Split,
            Orientation = orientation,
            FirstChild = firstChild,
            SecondChild = secondChild,
            Ratio = ratio
        };
    }

    public LayoutNode Clone()
    {
        return new LayoutNode
        {
            Type = Type,
            Orientation = Orientation,
            Ratio = Ratio,
            PanelId = PanelId,
            FirstChild = FirstChild?.Clone(),
            SecondChild = SecondChild?.Clone()
        };
    }

    public LayoutNode? FindNode(string panelId)
    {
        if (Type == LayoutNodeType.Leaf)
        {
            return string.Equals(PanelId, panelId, StringComparison.OrdinalIgnoreCase) ? this : null;
        }

        return FirstChild?.FindNode(panelId) ?? SecondChild?.FindNode(panelId);
    }

    public LayoutNode? FindParentOf(LayoutNode target)
    {
        if (Type == LayoutNodeType.Leaf)
            return null;

        if (FirstChild == target || SecondChild == target)
            return this;

        return FirstChild?.FindParentOf(target) ?? SecondChild?.FindParentOf(target);
    }

    public LayoutNode? FindParentOfPanel(string panelId)
    {
        var targetNode = FindNode(panelId);
        return targetNode == null ? null : FindParentOf(targetNode);
    }

    public void CollectPanelIds(ICollection<string> panelIds)
    {
        if (Type == LayoutNodeType.Leaf)
        {
            if (!string.IsNullOrEmpty(PanelId))
            {
                panelIds.Add(PanelId);
            }
        }
        else
        {
            FirstChild?.CollectPanelIds(panelIds);
            SecondChild?.CollectPanelIds(panelIds);
        }
    }

    public List<string> GetAllPanelIds()
    {
        var list = new List<string>();
        CollectPanelIds(list);
        return list;
    }

    /// <summary>
    /// Removes a panel by ID from this subtree.
    /// Returns the new root for this subtree (which may be a surviving child or null if empty).
    /// </summary>
    public static LayoutNode? Remove(LayoutNode? root, string panelId)
    {
        if (root == null)
            return null;

        if (root.Type == LayoutNodeType.Leaf)
        {
            return string.Equals(root.PanelId, panelId, StringComparison.OrdinalIgnoreCase) ? null : root;
        }

        // It is a split node. Recursively remove from children.
        var targetLeaf = root.FindNode(panelId);
        if (targetLeaf == null)
            return root; // Panel not found in this subtree

        var parent = root.FindParentOf(targetLeaf);
        if (parent == null)
        {
            // The root itself had targetLeaf as direct child? Handled below.
            parent = root;
        }

        var survivingChild = parent.FirstChild == targetLeaf ? parent.SecondChild : parent.FirstChild;

        if (parent == root)
        {
            // The split root lost one child, so the surviving child becomes the new root
            return survivingChild;
        }

        var grandParent = root.FindParentOf(parent);
        if (grandParent != null && survivingChild != null)
        {
            if (grandParent.FirstChild == parent)
                grandParent.FirstChild = survivingChild;
            else if (grandParent.SecondChild == parent)
                grandParent.SecondChild = survivingChild;
        }

        return root;
    }

    /// <summary>
    /// Splits an existing leaf node into a split node with the old panel and the new panel.
    /// </summary>
    public bool SplitLeaf(
        string targetPanelId,
        string newPanelId,
        SplitOrientation orientation,
        bool insertBefore,
        double ratio = 0.5)
    {
        var target = FindNode(targetPanelId);
        if (target == null || target.Type != LayoutNodeType.Leaf)
            return false;

        var existingPanel = target.PanelId!;
        var existingLeaf = CreateLeaf(existingPanel);
        var newLeaf = CreateLeaf(newPanelId);

        target.Type = LayoutNodeType.Split;
        target.Orientation = orientation;
        target.Ratio = ratio;
        target.PanelId = null;

        if (insertBefore)
        {
            target.FirstChild = newLeaf;
            target.SecondChild = existingLeaf;
        }
        else
        {
            target.FirstChild = existingLeaf;
            target.SecondChild = newLeaf;
        }

        return true;
    }
}
