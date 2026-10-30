# python -m unittest test_sounds  (no voice needed)
import unittest
from sounds import has_marks, phonemes


def fake(text):
    return "<" + text.strip() + ">"


class Sounds(unittest.TestCase):
    def test_a_marked_name_goes_in_as_its_sounds(self):
        self.assertEqual(phonemes("Up next, Chandelier by [Sia](/sˈiːə/).", fake), "<Up next, Chandelier by> sˈiːə <.>")

    def test_two_names_and_nothing_around_them(self):
        self.assertEqual(phonemes("[Jay-Z](/dʒˈeɪ zˈiː/) and [Rihanna](/ɹiːˈænə/)", fake), "dʒˈeɪ zˈiː <and> ɹiːˈænə")

    def test_no_marks_is_just_the_reader(self):
        self.assertFalse(has_marks("Up next, Firestarter."))
        self.assertTrue(has_marks("by [Sia](/sˈiːə/)"))


if __name__ == "__main__":
    unittest.main()
