-- Unified users: one login table (User_Account) for everyone who signs in.
-- Each login can point to the person it belongs to:
--   staff roles     -> Employee_ID   (already there)
--   Volunteer role  -> Volunteer_ID  (new, saved role name is 'ReadOnly')
--   Customer role   -> Customer_ID   (new)
-- A login belongs to at most one person, and each person has at most one login.
--
-- Run this ONCE on the zoo database (Team4-Zoo-Database on Aiven) with that database selected,
-- BEFORE the matching website code is pushed to main.

ALTER TABLE User_Account
  ADD COLUMN Volunteer_ID INT NULL AFTER Employee_ID,
  ADD COLUMN Customer_ID  INT NULL AFTER Volunteer_ID,
  ADD UNIQUE KEY uq_user_volunteer (Volunteer_ID),
  ADD UNIQUE KEY uq_user_customer  (Customer_ID),
  ADD CONSTRAINT fk_user_volunteer FOREIGN KEY (Volunteer_ID) REFERENCES Volunteer (Volunteer_ID)
      ON DELETE SET NULL ON UPDATE CASCADE,
  ADD CONSTRAINT fk_user_customer  FOREIGN KEY (Customer_ID)  REFERENCES Customer (Customer_ID)
      ON DELETE SET NULL ON UPDATE CASCADE;

-- The rules are checked with triggers instead of CHECK constraints, because MySQL does not
-- allow a CHECK on a column whose foreign key has ON DELETE SET NULL.
-- (Deleting a customer or volunteer still works: MySQL doesn't run triggers for the
-- ON DELETE SET NULL. Their login then shows "Needs link" on Manage Users.)
DROP TRIGGER IF EXISTS trg_user_account_person_ins;
DROP TRIGGER IF EXISTS trg_user_account_person_upd;

DELIMITER $$

CREATE TRIGGER trg_user_account_person_ins
BEFORE INSERT ON User_Account
FOR EACH ROW
BEGIN
  IF (NEW.Employee_ID IS NOT NULL) + (NEW.Volunteer_ID IS NOT NULL) + (NEW.Customer_ID IS NOT NULL) > 1 THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'A login can belong to only one person (employee, volunteer or customer).';
  END IF;
  IF NEW.Role = 'Customer' AND NEW.Customer_ID IS NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'A Customer login must be linked to a customer.';
  END IF;
  IF NEW.Role <> 'Customer' AND NEW.Customer_ID IS NOT NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Only a Customer login can be linked to a customer.';
  END IF;
  IF NEW.Role <> 'ReadOnly' AND NEW.Volunteer_ID IS NOT NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Only a Volunteer login can be linked to a volunteer.';
  END IF;
END$$

CREATE TRIGGER trg_user_account_person_upd
BEFORE UPDATE ON User_Account
FOR EACH ROW
BEGIN
  IF (NEW.Employee_ID IS NOT NULL) + (NEW.Volunteer_ID IS NOT NULL) + (NEW.Customer_ID IS NOT NULL) > 1 THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'A login can belong to only one person (employee, volunteer or customer).';
  END IF;
  IF NEW.Role = 'Customer' AND NEW.Customer_ID IS NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'A Customer login must be linked to a customer.';
  END IF;
  IF NEW.Role <> 'Customer' AND NEW.Customer_ID IS NOT NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Only a Customer login can be linked to a customer.';
  END IF;
  IF NEW.Role <> 'ReadOnly' AND NEW.Volunteer_ID IS NOT NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Only a Volunteer login can be linked to a volunteer.';
  END IF;
END$$

DELIMITER ;
