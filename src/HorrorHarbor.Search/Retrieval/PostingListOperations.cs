namespace HorrorHarbor.Search.Retrieval;

internal static class PostingListOperations
{
    public static int[] Intersect(
        ReadOnlySpan<int> left,
        ReadOnlySpan<int> right)
    {
        if (left.IsEmpty || right.IsEmpty)
        {
            return [];
        }

        int maximumLength = Math.Min(left.Length, right.Length);
        var result = new int[maximumLength];
        int leftIndex = 0;
        int rightIndex = 0;
        int resultIndex = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            int leftValue = left[leftIndex];
            int rightValue = right[rightIndex];
            if (leftValue < rightValue)
            {
                leftIndex++;
            }
            else if (leftValue > rightValue)
            {
                rightIndex++;
            }
            else
            {
                result[resultIndex++] = leftValue;
                leftIndex++;
                rightIndex++;
            }
        }

        return Trim(result, resultIndex);
    }

    public static int[] Union(
        ReadOnlySpan<int> left,
        ReadOnlySpan<int> right)
    {
        if (left.IsEmpty)
        {
            return right.ToArray();
        }

        if (right.IsEmpty)
        {
            return left.ToArray();
        }

        var result = new int[left.Length + right.Length];
        int leftIndex = 0;
        int rightIndex = 0;
        int resultIndex = 0;

        while (leftIndex < left.Length || rightIndex < right.Length)
        {
            int value;
            if (rightIndex >= right.Length ||
                leftIndex < left.Length && left[leftIndex] < right[rightIndex])
            {
                value = left[leftIndex++];
            }
            else if (leftIndex >= left.Length || right[rightIndex] < left[leftIndex])
            {
                value = right[rightIndex++];
            }
            else
            {
                value = left[leftIndex];
                leftIndex++;
                rightIndex++;
            }

            if (resultIndex == 0 || result[resultIndex - 1] != value)
            {
                result[resultIndex++] = value;
            }
        }

        return Trim(result, resultIndex);
    }

    public static int LowerBound(
        ReadOnlySpan<string> sortedValues,
        string value)
    {
        int lower = 0;
        int upper = sortedValues.Length;
        while (lower < upper)
        {
            int middle = lower + ((upper - lower) / 2);
            if (StringComparer.Ordinal.Compare(sortedValues[middle], value) < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return lower;
    }

    private static int[] Trim(int[] values, int length)
    {
        if (length == 0)
        {
            return [];
        }

        if (length != values.Length)
        {
            Array.Resize(ref values, length);
        }

        return values;
    }
}
