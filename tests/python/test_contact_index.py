from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'worker'))
from contact_index import ContactIndex

class ContactIndexTests(unittest.TestCase):
    def test_substring_unicode_literal_symbols_and_recency(self):
        index = ContactIndex([
            ('p1', 'Bristol', '', '', None, 1),
            ('p2', 'bristol', '超级答题', '', None, 9),
            ('p3', 'contains %_', '', '', None, 3),
        ])
        self.assertEqual(['p2', 'p1'], [p['id'] for p in index.search('BRIS')])
        self.assertEqual(['p2'], [p['id'] for p in index.search('级答')])
        self.assertEqual(['p3'], [p['id'] for p in index.search('%_')])
        self.assertEqual([], index.search('risbris'))

    def test_ngram_candidates_are_rechecked_and_limits_apply(self):
        index = ContactIndex([('a', 'abXbc', '', '', None, 0), ('b', 'abc', '', '', None, 0)])
        self.assertEqual(['b'], [p['id'] for p in index.search('abc')])
        self.assertEqual(1, len(index.search('', 1)))

if __name__ == '__main__':
    unittest.main()
