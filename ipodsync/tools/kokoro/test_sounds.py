# python -m unittest test_sounds  (no voice needed)
import unittest
from sounds import has_marks, phonemes, to_misaki, marks_for_misaki


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

    # Kokoro's own alphabet: espeak's "dʒ eɪ" made Jay-Z come out as "jyz" (owner, 30 Oct)
    def test_espeak_spellings_become_kokoros_own(self):
        self.assertEqual(to_misaki("dʒˈeɪ zˈiː"), "ʤˈA zˈi")
        self.assertEqual(to_misaki("ˈɛmɪnˌɛm"), "ˈɛmɪnˌɛm")
        self.assertEqual(to_misaki("hˈoʊziɚ"), "hˈOziəɹ")
        self.assertEqual(to_misaki("kˈaɪɡoʊ"), "kˈIɡO")

    def test_british_voices_keep_long_vowels_and_their_oh(self):
        self.assertEqual(to_misaki("kˈaɪɡəʊ", british=True), "kˈIɡQ")
        self.assertEqual(to_misaki("sˈiːə", british=True), "sˈiːə")

    def test_already_kokoros_alphabet_is_left_alone(self):
        self.assertEqual(to_misaki("ʤˈA zˈi"), "ʤˈA zˈi")

    def test_marks_stay_marked_for_misaki(self):
        self.assertEqual(marks_for_misaki("Next, [Jay-Z](/dʒˈeɪ zˈiː/), Empire State."), "Next, [Jay-Z](/ʤˈA zˈi/), Empire State.")


if __name__ == "__main__":
    unittest.main()
