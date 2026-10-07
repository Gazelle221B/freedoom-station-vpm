"""Guard the guest fixtures' independent checks and virtual data layout."""
import unittest
import mprv_guest_tests as guests


class GuestFixtures(unittest.TestCase):
    def test_return_matrix_has_nine_distinct_fixtures(self):
        cases = guests.cases()
        self.assertEqual(len(cases), 9)
        self.assertEqual(len({name for name, _ in cases}), 9)

    def test_mret_clear_and_retain_are_opposite_checks(self):
        _, clear, _ = guests.return_case('mret', 1)
        _, retain, _ = guests.return_case('mret', 3)
        self.assertIn('bnez t0, fail', clear)
        self.assertIn('beqz t0, fail', retain)
        self.assertIn('csrr t0, mcause', clear)
        self.assertIn('li t1, 9', clear)

    def test_sret_from_machine_really_sets_mprv_in_guest(self):
        body, handler, _ = guests.return_case('sret', 1)
        self.assertIn('li t0, 131328', body)  # MPRV | SPP
        self.assertIn('csrw mstatus, t0\nsret', body)
        self.assertIn('bnez t0, fail', handler)

    def test_physical_code_and_virtual_load_are_separate(self):
        body, handler, data = guests.load_case(1)
        self.assertIn('csrw satp, t0', body)
        self.assertIn('li t0, 0x00400000\nlw', body)
        self.assertIn('bne t1, t2, fail', body)
        self.assertEqual(handler, 'j fail\n')  # every unexpected trap fails
        self.assertIn('.org 0x1000\n.word 0\n.word 0x20000801', data)
        self.assertIn('.org 0x4000\n.word 0x12345678', data)


if __name__ == '__main__':
    unittest.main()
