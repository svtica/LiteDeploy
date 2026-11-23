// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ListViewColumnSorter.cs
// Author:  LiteDeploy contributors
// Date:    2025-05-30
// Purpose: IComparer used to sort the columns of a ListView.
// -----------------------------------------------------------------------------
using System.Collections;
using System.Linq;
using System.Windows.Forms;

namespace LiteDeploy
{
    /// <summary>
    /// This class is an implementation of the 'IComparer' interface.
    /// </summary>
    public class ListViewColumnSorter : IComparer
    {
        /// <summary>
        /// Specifies the column to be sorted
        /// </summary>
        private int ColumnToSort;
        /// <summary>
        /// Specifies the order in which to sort (i.e. 'Ascending').
        /// </summary>
        private SortOrder OrderOfSort;
        /// <summary>
        /// Case insensitive comparer object
        /// </summary>
        private CaseInsensitiveComparer ObjectCompare;

        /// <summary>
        /// Class constructor.  Initializes various elements
        /// </summary>
        public ListViewColumnSorter()
        {
            // Initialize the column to '0'
            ColumnToSort = 0;

            // Initialize the sort order to 'none'
            OrderOfSort = SortOrder.None;

            // Initialize the CaseInsensitiveComparer object
            ObjectCompare = new CaseInsensitiveComparer();
        }

        /// <summary>
        /// This method is inherited from the IComparer interface.  It compares the two objects passed using a case insensitive comparison.
        /// </summary>
        /// <param name="x">First object to be compared</param>
        /// <param name="y">Second object to be compared</param>
        /// <returns>The result of the comparison. "0" if equal, negative if 'x' is less than 'y' and positive if 'x' is greater than 'y'</returns>
        public int Compare(object x, object y)
        {
            int compareResult;
            ListViewItem listviewX, listviewY;

            // Cast the objects to be compared to ListViewItem objects
            listviewX = (ListViewItem)x;
            listviewY = (ListViewItem)y;

            // Compare the two items
            compareResult = CompareTexts(listviewX.SubItems[ColumnToSort].Text, listviewY.SubItems[ColumnToSort].Text);

            // Calculate correct return value based on object comparison
            if (OrderOfSort == SortOrder.Ascending)
            {
                // Ascending sort is selected, return normal result of compare operation
                return compareResult;
            }
            else if (OrderOfSort == SortOrder.Descending)
            {
                // Descending sort is selected, return negative result of compare operation
                return (-compareResult);
            }
            else
            {
                // Return '0' to indicate they are equal
                return 0;
            }
        }

        /// <summary>
        /// Two cell texts: numbers (RAM, cores, disk: "8" before "16") by value, IPv4 addresses (the first of the
        /// cell: "192.0.2.9" before "192.0.2.10") by their four numbers, anything else without regard to case.
        /// Unit-tested.
        /// </summary>
        public static int CompareTexts(string a, string b)
        {
            long ipA, ipB;
            if (TryIPv4(a, out ipA) && TryIPv4(b, out ipB)) return ipA.CompareTo(ipB);
            double x, y;
            if (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                && double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                return x.CompareTo(y);
            return CaseInsensitiveComparer.Default.Compare(a, b);
        }

        /// <summary>The first address of a cell ("a.b.c.d" or "a.b.c.d, ...") as a number; false when it is not one.</summary>
        static bool TryIPv4(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string first = text.Split(new[] { ',', ' ', ';' }, System.StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first == null) return false;
            string[] parts = first.Split('.');
            if (parts.Length != 4) return false;
            foreach (string p in parts)
            {
                int n;
                if (p.Length == 0 || p.Length > 3 || !int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n) || n > 255)
                    return false;
                value = value * 256 + n;
            }
            return true;
        }

        /// <summary>
        /// Gets or sets the number of the column to which to apply the sorting operation (Defaults to '0').
        /// </summary>
        public int SortColumn
        {
            set
            {
                ColumnToSort = value;
            }
            get
            {
                return ColumnToSort;
            }
        }

        /// <summary>
        /// Gets or sets the order of sorting to apply (for example, 'Ascending' or 'Descending').
        /// </summary>
        public SortOrder Order
        {
            set
            {
                OrderOfSort = value;
            }
            get
            {
                return OrderOfSort;
            }
        }

    }
}
